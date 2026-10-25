using System.ComponentModel;
using Echo.Extensions;
using Echo.Listening;
using Echo.Setup;
using Echo.Setup.Hardware;
using Echo.Setup.Hotkeys;
using Echo.Setup.Steps;
using Echo.UI;
using Echo.Updates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Core;
using Spectre.Console;
using Spectre.Console.Cli;
using Whisper.net.LibraryLoader;

namespace Echo.Commands;

/// <summary>
///     The default command: shows the startup screen, runs the setup wizard when it has not been completed yet,
///     and then runs push-to-talk voice typing on the working screen until the user leaves it.
/// </summary>
/// <param name="console">The console to draw on.</param>
/// <param name="paths"><see cref="IAppPaths"/></param>
/// <param name="setupRunner"><see cref="SetupRunner"/></param>
/// <param name="audioInput">The microphones, for the name of the one in use.</param>
/// <param name="terminalFocus">Tells whether Echo's own window is in front.</param>
/// <param name="focusReports">What the terminal itself says about its window, where the system does not say it.</param>
/// <param name="updater">Finds and downloads newer versions while the working screen runs.</param>
/// <param name="versionFolders">Tells the line that installs Echo, for an Echo that cannot update itself.</param>
internal sealed class ListenCommand(
    IAnsiConsole console,
    IAppPaths paths,
    SetupRunner setupRunner,
    IAudioInput audioInput,
    ITerminalFocus terminalFocus,
    TerminalFocusReports? focusReports = null,
    IUpdater? updater = null,
    IVersionFolders? versionFolders = null) : AsyncCommand<ListenCommand.ListenSettings>
{
    internal const string Name = "listen";
    internal const string Description = "Starts push-to-talk voice typing (default).";

    /// <summary>
    ///     Returned when the processor cannot run recognition.
    /// </summary>
    internal const int UnsupportedProcessorExitCode = 3;

    /// <summary>
    ///     How often the working screen is refreshed: often enough for the equalizer to move smoothly.
    /// </summary>
    private static readonly TimeSpan ScreenTick = TimeSpan.FromMilliseconds(50);

    /// <summary>
    ///     Model files are named "ggml-tiny.bin"; the state line names them "tiny".
    /// </summary>
    private const string ModelPrefix = "ggml-";

    private readonly IAnsiConsole _console = console;
    private readonly IAppPaths _paths = paths;
    private readonly SetupRunner _setupRunner = setupRunner;
    private readonly IAudioInput _audioInput = audioInput;
    private readonly ITerminalFocus _terminalFocus = terminalFocus;
    private readonly TerminalFocusReports? _focusReports = focusReports;
    private readonly IUpdater? _updater = updater;
    private readonly IVersionFolders? _versionFolders = versionFolders;

    /// <summary>
    ///     Done once the start-up of a working screen is through: the one after a trip to the setup wizard counts too,
    ///     when the user left the first before it was.
    /// </summary>
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     Command line options of <see cref="ListenCommand"/>.
    /// </summary>
    internal sealed class ListenSettings : CommandSettings
    {
        [CommandOption(Relaunching.NoBannerOption)]
        [Description("Skips the startup animation.")]
        public bool NoBanner { get; init; }

        /// <summary>
        ///     The version that /restart started Echo from, which may have been an update.
        /// </summary>
        [CommandOption(Relaunching.UpdatedFromOption, IsHidden = true)]
        public string? UpdatedFrom { get; init; }
    }

    /// <inheritdoc/>
    public override async Task<int> ExecuteAsync(CommandContext context,
                                                 ListenSettings settings,
                                                 CancellationToken cancellationToken)
    {
        if (!settings.NoBanner)
            await new StartupScreen(_console).PlayAsync(cancellationToken);

        // Checked at every start, not only in setup: the settings may have come from another computer
        if (ProcessorSupport.MissingInstructions() is { Count: > 0 } missing)
        {
            _console.MarkupLine($"  [{Theme.Failure}]{Markup.Escape(Strings.Format(nameof(Strings.ProcessorUnsupported), string.Join(", ", missing)))}[/]");
            _console.MarkupLine($"  {Markup.Escape(Strings.ProcessorUnsupportedHint)}");
            _console.WriteLine();

            return UnsupportedProcessorExitCode;
        }

        if (_setupRunner.IsSetupRequired())
        {
            // Without a terminal nobody can answer the wizard, so the reason is printed instead
            if (!_console.Profile.Capabilities.Interactive)
            {
                _console.WriteLine(Strings.SetupNeedsTerminal);

                return SetupCommand.NoTerminalExitCode;
            }

            await _setupRunner.RunAsync(onlyNewSteps: true, cancellationToken);

            Languages.Apply(new UserSettingsStore(_paths).Load().UiLanguage);
        }

        // Without a terminal to draw on, the log is simply written out, as in earlier versions
        if (!_console.Profile.Capabilities.Interactive)
            return await RunPlainAsync(cancellationToken);

        // The log of the screen outlives a trip to the setup wizard, so what happened before stays in sight
        var log = new ScreenLog();

        bool isJustUpdated = settings.UpdatedFrom is { } from && ReleaseVersion.IsNewer(ApplicationInfo.Version, from);

        if (isJustUpdated)
            log.Add(EntryKind.Answer, Strings.Format(nameof(Strings.UpdateApplied), settings.UpdatedFrom, ApplicationInfo.Version));

        // The test dictation of the setup wizard may have loaded a runtime that the final choice does not use
        if (IsRestartNeeded())
            log.Add(EntryKind.Warning, Strings.SetupRestartNeeded);

        void Tell(object? sender, UpdateNotice notice) => ListenCommands.Tell(log, notice);

        if (_updater is not null)
        {
            _updater.Noticed += Tell;
            _updater.Start(isJustUpdated, _started.Task);
        }

        try
        {
            return await RunScreensAsync(log, cancellationToken);
        }
        finally
        {
            if (_updater is not null)
                _updater.Noticed -= Tell;
        }
    }

    /// <summary>
    ///     Runs the working screen, and the setup wizard in between when the user asks for it, until the user leaves.
    /// </summary>
    private async Task<int> RunScreensAsync(ScreenLog log, CancellationToken cancellationToken)
    {
        while (true)
        {
            ListenOutcome outcome = await RunScreenAsync(log, cancellationToken);

            // The screen and its services are stopped by now, so the key and the microphone are free for the new start
            if (outcome == ListenOutcome.Restart)
                return Relaunching.ExitCode;

            if (outcome != ListenOutcome.Setup)
                return 0;

            SetupOutcome setup = await _setupRunner.RunAsync(onlyNewSteps: false, cancellationToken);

            Languages.Apply(new UserSettingsStore(_paths).Load().UiLanguage);

            if (setup == SetupOutcome.Cancelled)
                log.Add(EntryKind.Answer, Strings.SetupUnchanged);
            else if (IsRestartNeeded())
                log.Add(EntryKind.Warning, Strings.SetupRestartNeeded);
            else
                log.Add(EntryKind.Answer, Strings.ListenSetupDone);
        }
    }

    /// <summary>
    ///     Whether the acceleration chosen in the setup wizard takes effect only in a fresh start: the recognition
    ///     runtime is loaded once per process. Until then Echo goes on with the one it has, and the log says so.
    /// </summary>
    private bool IsRestartNeeded() =>
        _setupRunner.ChosenBackend is { } backend && Acceleration.NeedsRestart(backend, RuntimeOptions.LoadedLibrary);

    /// <summary>
    ///     Runs dictation with the working screen until the user leaves it. The model loads while the screen
    ///     is already up, so the user sees it happen.
    /// </summary>
    private async Task<ListenOutcome> RunScreenAsync(ScreenLog log, CancellationToken cancellationToken)
    {
        IConfiguration configuration = ServiceCollectionExtensions.BuildConfiguration(_paths);

        var levelSwitch = new LoggingLevelSwitch(ServiceCollectionExtensions.LogLevelOf(configuration));
        // Echo's window counts as having the keyboard only when that is known: otherwise a phrase
        // is typed into the window in front, as it would be without the screen
        var activity = new ListenActivity(log, () => _terminalFocus.IsFocused() == true);

        _ = activity.Started.ContinueWith(_ => _started.TrySetResult(), TaskScheduler.Default);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Services.AddSingleton(_paths);
        builder.Services.AddPlatform(_paths);
        builder.Services.AddSingleton<IListenActivity>(activity);
        builder.Services.SetupConfiguration(configuration);
        builder.Services.ConfigureScreenLogging(configuration, levelSwitch, log, _paths.LogsDirectory);
        builder.Services.RegisterServices();

        using IHost host = builder.Build();

        // The same service pastes the phrases: a copying waits for a phrase being pasted, and the other way round
        var commands = new ListenCommands(
            activity,
            levelSwitch,
            new UserSettingsStore(_paths),
            () => host.Services.GetRequiredService<ITextInsertionService>(),
            () => SummarizeSettings(configuration, activity, levelSwitch),
            _updater,
            _versionFolders?.InstallCommand ?? string.Empty);

        var screen = new ListenScreen(
            _console,
            Theme.Detect(_console),
            activity,
            commands,
            DescribeSettings(configuration),
            ScreenTick,
            _focusReports,
            _updater is null ? null : () => _updater.Status);

        Task starting = Task.Run(() => StartAsync(host, activity, cancellationToken), cancellationToken);

        using CancellationTokenSource running = ScreenLifetime(host, cancellationToken);

        try
        {
            return await screen.RunAsync(running.Token);
        }
        finally
        {
            try
            {
                // A copying still under way ends on this screen, before the services it uses are stopped
                await commands.Copying;
                await starting;
                await host.StopAsync(CancellationToken.None);
            }
            catch (Exception exc) when (exc is OperationCanceledException or AggregateException)
            {
                // Leaving while the model is still loading: there is nothing left to stop
            }

            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    ///     How long the working screen runs: until the user leaves it, or until the system asks Echo to stop. A stop
    ///     signal, such as the one of <c>kill</c> or of the system shutting down, is taken by the host, which only asks
    ///     itself to stop and keeps the process alive: the screen then ends as with /quit, and Echo with it.
    /// </summary>
    internal static CancellationTokenSource ScreenLifetime(IHost host, CancellationToken cancellationToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);

    /// <summary>
    ///     Starts the services. One that cannot even be created, for a setting it cannot take, say, is shown
    ///     on the screen like any start that failed, instead of leaving the screen at "starting".
    /// </summary>
    internal static async Task StartAsync(IHost host, IListenActivity activity, CancellationToken cancellationToken)
    {
        try
        {
            await host.StartAsync(cancellationToken);
        }
        catch (Exception exc) when (exc is not OperationCanceledException)
        {
            activity.Log.Add(EntryKind.Error, exc.Message);
            activity.Log.Add(EntryKind.Answer, Strings.ListenFailedHint);
            activity.SetState(ListenState.Failed);
        }
    }

    /// <summary>
    ///     What the state line says about the settings: the model and the key.
    /// </summary>
    private static ListenInfo DescribeSettings(IConfiguration configuration)
    {
        string model = Path.GetFileNameWithoutExtension(Setting(configuration, nameof(WhisperSettings), nameof(WhisperSettings.ModelName)) ?? string.Empty);

        if (model.StartsWith(ModelPrefix, StringComparison.OrdinalIgnoreCase))
            model = model[ModelPrefix.Length..];

        float silenceLevel = float.TryParse(
            Setting(configuration, nameof(RecordingDeviceSettings), nameof(RecordingDeviceSettings.SilenceThreshold)),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out float parsed)
            ? parsed
            : 0f;

        return new ListenInfo(model, KeyName(configuration, isShort: true), silenceLevel);
    }

    /// <summary>
    ///     The main settings Echo runs with, for the /info command.
    /// </summary>
    private List<(string Label, string Value)> SummarizeSettings(IConfiguration configuration,
                                                                IListenActivity activity,
                                                                LoggingLevelSwitch levelSwitch)
    {
        string? uiLanguage = new UserSettingsStore(_paths).Load().UiLanguage;
        string? dictationLanguage = Setting(configuration, nameof(WhisperSettings), nameof(WhisperSettings.Language));

        // What the model runs on is known once it is loaded; "automatic" says what it turned out to be
        string runtime = activity.Runtime ?? "…";
        string backend = Acceleration.Parse(Setting(configuration, nameof(WhisperSettings), nameof(WhisperSettings.HardwareBackend))) is null
            ? $"{Strings.AccelerationAuto} ({runtime})"
            : runtime;

        bool isAutoInsert = IsOn(nameof(TextInsertionSettings), nameof(TextInsertionSettings.UseAutoInsert));
        bool isAutoUpdate = new UserSettingsStore(_paths).Load().Additional.IsOn(AdditionalSettings.AutoUpdate);
        UpdateStatus version = _updater?.Status ?? new UpdateStatus(UpdateStage.Unknown, ApplicationInfo.Version);
        bool isSoundOn = IsOn(nameof(PushToTalkSettings), nameof(PushToTalkSettings.PlaySound));

        double volume = double.TryParse(
            Setting(configuration, nameof(PushToTalkSettings), nameof(PushToTalkSettings.Volume)),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double parsed)
            ? parsed
            : 0;

        double threshold = double.TryParse(
            Setting(configuration, nameof(RecordingDeviceSettings), nameof(RecordingDeviceSettings.SilenceThreshold)),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double level)
            ? level
            : 0;

        return
        [
            (Strings.SummaryInterfaceLanguage, Languages.Interface.FirstOrDefault(language => language.Code == uiLanguage).Name ?? "—"),
            (Strings.SummaryDictationLanguage, Languages.DictationName(dictationLanguage)),
            (Strings.SummaryModel, Setting(configuration, nameof(WhisperSettings), nameof(WhisperSettings.ModelName)) ?? "—"),
            (Strings.SummaryAcceleration, backend),
            (Strings.SummaryHotkey, KeyName(configuration)),
            (Strings.SummaryMicrophone, MicrophoneName(configuration) ?? "—"),
            (Strings.SubCalibration, threshold.ToString("0.000", System.Globalization.CultureInfo.CurrentUICulture)),
            (Strings.AdditionalAutoInsert, isAutoInsert ? Strings.ValueOn : Strings.ValueOff),
            (Strings.AdditionalPlaySound, isSoundOn ? $"{Strings.ValueOn}, {Math.Round(volume * 100)} %" : Strings.ValueOff),
            (Strings.AdditionalLogLevel, ListenCommands.Levels[(int)levelSwitch.MinimumLevel]),
            (Strings.SummaryVersion, ListenCommands.DescribeVersion(version)),
            (Strings.AdditionalAutoUpdate, isAutoUpdate ? Strings.ValueOn : Strings.ValueOff),
            (Strings.InfoSettingsFile, _paths.SettingsFile),
            (Strings.InfoLogsFolder, _paths.LogsDirectory),
        ];

        bool IsOn(string section, string key) =>
            !bool.TryParse(Setting(configuration, section, key), out bool value) || value;
    }

    private static string KeyName(IConfiguration configuration, bool isShort = false) =>
        HotkeyNames.Display(
            new HotkeyChoice(
                Setting(configuration, nameof(PushToTalkSettings), nameof(PushToTalkSettings.Key)) ?? HotkeyChoice.Default.Key,
                Setting(configuration, nameof(PushToTalkSettings), nameof(PushToTalkSettings.Modifier)) ?? string.Empty),
            Setting(configuration, nameof(PushToTalkSettings), nameof(PushToTalkSettings.DesktopShortcut)),
            isShort);

    private static string? Setting(IConfiguration configuration, string section, string key) =>
        configuration[$"{section}:{key}"];

    /// <summary>
    ///     The name the setup wizard saw for the microphone, or the one the system gives the device now.
    /// </summary>
    private string? MicrophoneName(IConfiguration configuration)
    {
        if (new UserSettingsStore(_paths).Load().MicrophoneName is { Length: > 0 } saved)
            return saved;

        try
        {
            int index = int.TryParse(configuration[$"{nameof(RecordingDeviceSettings)}:{nameof(RecordingDeviceSettings.DeviceIndex)}"], out int parsed)
                ? parsed
                : 0;

            return _audioInput.GetDevices().FirstOrDefault(device => device.Index == index)?.Name;
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            // Asking the audio system is only for a name on the screen; whatever goes wrong there leaves it out
            return null;
        }
    }

    /// <summary>
    ///     Runs dictation writing its log out line by line, for a console nobody looks at.
    /// </summary>
    private async Task<int> RunPlainAsync(CancellationToken cancellationToken)
    {
        // Settings and bundled assets are found wherever the command is run from
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory
        });

        // The log reads its detail from the same settings the services run with
        IConfiguration configuration = ServiceCollectionExtensions.BuildConfiguration(_paths);

        builder.Services.AddSingleton(_paths);
        builder.Services.AddPlatform(_paths);
        builder.Services.SetupConfiguration(configuration);
        builder.Services.ConfigureSerilog(configuration, _paths.LogsDirectory);
        builder.Services.RegisterServices();

        var host = builder.Build();

        try
        {
            await host.RunAsync(cancellationToken);

            return 0;
        }
        catch (Exception exc)
        {
            Log.Fatal(exc, "{ExplosionEmoji} Application terminated unexpectedly",
                LoggerConstants.ExplosionEmoji);

            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}