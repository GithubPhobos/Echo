using System.ComponentModel;
using Echo.Extensions;
using Echo.Listening;
using Echo.Setup;
using Echo.Setup.Hardware;
using Echo.Setup.Hotkeys;
using Echo.Setup.Steps;
using Echo.UI;
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
internal sealed class ListenCommand(
    IAnsiConsole console,
    IAppPaths paths,
    SetupRunner setupRunner,
    IAudioInput audioInput,
    ITerminalFocus terminalFocus) : AsyncCommand<ListenCommand.ListenSettings>
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

    /// <summary>
    ///     Command line options of <see cref="ListenCommand"/>.
    /// </summary>
    internal sealed class ListenSettings : CommandSettings
    {
        [CommandOption("--no-banner")]
        [Description("Skips the startup animation.")]
        public bool NoBanner { get; init; }
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

            // The test dictation may have loaded a runtime that the final choice does not use
            if (_setupRunner.ChosenBackend is { } backend && Acceleration.NeedsRestart(backend, RuntimeOptions.LoadedLibrary))
            {
                _console.MarkupLine($"  [{Theme.Highlight}]{Markup.Escape(Strings.SetupRestartNeeded)}[/]");
                _console.WriteLine();

                return 0;
            }
        }

        // Without a terminal to draw on, the log is simply written out, as in earlier versions
        if (!_console.Profile.Capabilities.Interactive)
            return await RunPlainAsync(cancellationToken);

        // The log of the screen outlives a trip to the setup wizard, so what happened before stays in sight
        var log = new ScreenLog();

        while (true)
        {
            ListenOutcome outcome = await RunScreenAsync(log, cancellationToken);

            if (outcome != ListenOutcome.Setup)
                return 0;

            await _setupRunner.RunAsync(onlyNewSteps: false, cancellationToken);

            Languages.Apply(new UserSettingsStore(_paths).Load().UiLanguage);

            // A runtime is loaded once per process: another backend needs a fresh start
            if (_setupRunner.ChosenBackend is { } backend && Acceleration.NeedsRestart(backend, RuntimeOptions.LoadedLibrary))
            {
                _console.MarkupLine($"  [{Theme.Highlight}]{Markup.Escape(Strings.SetupRestartNeeded)}[/]");
                _console.WriteLine();

                return 0;
            }

            log.Add(EntryKind.Answer, Strings.ListenSetupDone);
        }
    }

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

        var screen = new ListenScreen(
            _console,
            Theme.Detect(_console),
            activity,
            new ListenCommands(activity, levelSwitch, new UserSettingsStore(_paths), () => SummarizeSettings(configuration, activity, levelSwitch)),
            DescribeSettings(configuration),
            ScreenTick);

        Task starting = Task.Run(() => StartAsync(host, activity, cancellationToken), cancellationToken);

        try
        {
            return await screen.RunAsync(cancellationToken);
        }
        finally
        {
            try
            {
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