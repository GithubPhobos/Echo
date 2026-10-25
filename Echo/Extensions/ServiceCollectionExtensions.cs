using Echo.Listening;
using Echo.Services.BackgroundServices;
using Echo.Setup;
using Echo.Setup.Audio;
using Echo.Setup.Check;
using Echo.Setup.Downloads;
using Echo.Setup.Hardware;
using Echo.Setup.Hotkeys;
using Echo.Setup.Steps;
using Echo.UI;
using Echo.Updates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Spectre.Console;

namespace Echo.Extensions;

/// <summary>
///     Extension methods for <see cref="IServiceCollection"/> to encapsulate and organize 
///     the application's dependency injection bootstrap logic.
/// </summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Configures Serilog as the primary logging provider, setting up console output 
    ///     with custom formatting and log levels.
    /// </summary>
    /// <param name="services"><see cref="IServiceCollection"/></param>
    /// <param name="configuration"><see cref="IConfiguration"/></param>
    /// <param name="logsDirectory">Where the files go.</param>
    internal static void ConfigureSerilog(this IServiceCollection services, IConfiguration configuration, string logsDirectory)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Error)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}",
                theme: AnsiConsoleTheme.Code)
            .WriteTo.Sink(new DailyFileSink(logsDirectory))
            .CreateLogger();

        services.AddSerilog();
    }

    /// <summary>
    ///     Configures Serilog for the working screen: messages go to the screen's log and to a file a day,
    ///     with a detail that can be changed while Echo runs.
    /// </summary>
    /// <param name="services"><see cref="IServiceCollection"/></param>
    /// <param name="configuration"><see cref="IConfiguration"/></param>
    /// <param name="levelSwitch">The log detail; it starts at the one in the settings.</param>
    /// <param name="log">The screen's log.</param>
    /// <param name="logsDirectory">Where the files go.</param>
    internal static void ConfigureScreenLogging(this IServiceCollection services,
                                                IConfiguration configuration,
                                                LoggingLevelSwitch levelSwitch,
                                                ScreenLog log,
                                                string logsDirectory)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .MinimumLevel.ControlledBy(levelSwitch)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Error)
            .Enrich.FromLogContext()
            .WriteTo.Sink(new ScreenLogSink(log))
            .WriteTo.Sink(new DailyFileSink(logsDirectory))
            .CreateLogger();

        services.AddSerilog();
    }

    /// <summary>
    ///     The log detail in the settings, or Debug, which Echo ships with.
    /// </summary>
    internal static LogEventLevel LogLevelOf(IConfiguration configuration) =>
        Enum.TryParse(configuration["Serilog:MinimumLevel:Default"], ignoreCase: true, out LogEventLevel level)
            ? level
            : LogEventLevel.Debug;

    /// <summary>
    ///     Registers the command line interface and the setup wizard.
    /// </summary>
    /// <param name="services"><see cref="IServiceCollection"/></param>
    /// <param name="paths"><see cref="IAppPaths"/></param>
    internal static void RegisterSetup(this IServiceCollection services, IAppPaths paths)
    {
        services.AddSingleton(paths);
        services.AddPlatform(paths);
        services.AddSingleton(AnsiConsole.Console);
        services.AddSingleton(SetupTimings.Default);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new SetupUi(
            provider.GetRequiredService<IAnsiConsole>(),
            Theme.Detect(provider.GetRequiredService<IAnsiConsole>()),
            provider.GetRequiredService<SetupTimings>().Tick));

        services.AddSingleton<IUserSettingsStore, UserSettingsStore>();
        services.AddSingleton<ISystemShell, SystemShell>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IHardwareProbe, HardwareProbe>();
        services.AddSingleton<IRecognitionRuntime, RecognitionRuntime>();
        services.AddSingleton<IFileDownloader>(_ => new FileDownloader(CreateDownloadClient()));
        services.AddSingleton<ICudaRuntime, CudaRuntime>();
        services.AddSingleton<IHotkeyCaptureFactory, HotkeyCaptureFactory>();
        services.AddSingleton<IMicrophoneProbe, MicrophoneProbe>();
        services.AddSingleton<IDictationTester, DictationTester>();

        // The wizard shows the steps in exactly this order; adding, removing or moving a step is one line here
        services.AddSingleton<ISetupStep, LanguageStep>();
        services.AddSingleton<ISetupStep, AccelerationStep>();
        services.AddSingleton<ISetupStep, ModelStep>();
        services.AddSingleton<ISetupStep, HotkeyStep>();
        services.AddSingleton<ISetupStep, CheckStep>();
        services.AddSingleton<ISetupStep, AdditionalSettingsStep>();

        services.AddSingleton<SetupRunner>();

        services.AddSingleton<IReleaseSource>(provider => new GitHubReleaseSource(
            CreateReleaseClient(),
            GitHubReleaseSource.Address(Environment.GetEnvironmentVariable),
            provider.GetRequiredService<IVersionFolders>().ArchiveName));

        services.AddSingleton<IUpdater>(provider => new Updater(
            provider.GetRequiredService<IReleaseSource>(),
            provider.GetRequiredService<IFileDownloader>(),
            provider.GetRequiredService<IVersionFolders>(),
            provider.GetRequiredService<IUserSettingsStore>(),
            provider.GetRequiredService<TimeProvider>(),
            InstallLayout.Of(AppContext.BaseDirectory),
            ApplicationInfo.Version,
            AppContext.BaseDirectory));
    }

    /// <summary>
    ///     Builds the application configuration: the defaults from the bundled 'appsettings.json',
    ///     then the user's own settings file, then <paramref name="overrides"/>.
    /// </summary>
    /// <param name="paths"><see cref="IAppPaths"/></param>
    /// <param name="overrides">Values that are not saved anywhere yet, e.g. the draft of the setup wizard.</param>
    internal static IConfiguration BuildConfiguration(IAppPaths paths,
                                                      IEnumerable<KeyValuePair<string, string?>>? overrides = null)
    {
        IConfigurationBuilder builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true);

        // A damaged settings file counts as no settings at all, as it does for the setup wizard,
        // which then writes a fresh one: it must not stop anything before that
        if (UserSettingsStore.IsReadable(paths.SettingsFile))
        {
            builder.AddJsonFile(paths.SettingsFile, optional: true);

            // A value a setting cannot take is replaced by the shipped one, the way the setup wizard reads it
            builder.AddInMemoryCollection(UserSettingsStore.Rejected(paths.SettingsFile));
        }

        return builder
            .AddInMemoryCollection(overrides)
            .Build();
    }

    /// <summary>
    ///     Binds the configuration sections to strongly-typed options classes.
    /// </summary>
    /// <param name="services"><see cref="IServiceCollection"/></param>
    /// <param name="config"><see cref="IConfiguration"/></param>
    internal static void SetupConfiguration(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<WhisperSettings>(config.GetSection(nameof(WhisperSettings)));
        services.Configure<PushToTalkSettings>(config.GetSection(nameof(PushToTalkSettings)));
        services.Configure<TextInsertionSettings>(config.GetSection(nameof(TextInsertionSettings)));
        services.Configure<RecordingDeviceSettings>(config.GetSection(nameof(RecordingDeviceSettings)));
    }

    /// <summary>
    ///     Registers all application-specific services, singletons, and background hosted services 
    ///     into the dependency injection container.
    /// </summary>
    /// <param name="services"><see cref="IServiceCollection"/></param>
    internal static void RegisterServices(this IServiceCollection services)
    {
        services.AddHostedService<EchoOrchestrator>();

        services.RegisterDictationServices();
    }

    /// <summary>
    ///     Registers the dictation pipeline itself, without the background service that drives it.
    ///     The setup wizard uses it to try the settings before they are saved.
    /// </summary>
    /// <param name="services"><see cref="IServiceCollection"/></param>
    internal static void RegisterDictationServices(this IServiceCollection services)
    {
        services.AddSingleton<IAssetsProvider, AssetsProvider>();
        services.AddSingleton<ISoundPlayService, SoundPlayService>();
        services.AddSingleton<ITextInsertionService, TextInsertionService>();
        services.AddSingleton<IAudioRecordingService, AudioRecordingService>();
        services.AddSingleton<IWhisperInferenceService, WhisperInferenceService>();
        services.AddSingleton<IPushToTalkMonitorService, PushToTalkMonitorService>();
    }

    /// <summary>
    ///     The client that asks for the latest release. An answer that takes longer than a few seconds is not waited
    ///     for: the next check asks again.
    /// </summary>
    private static HttpClient CreateReleaseClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // GitHub turns away requests that do not say who sends them
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{ApplicationInfo.CommandName}/{ApplicationInfo.Version}");

        return client;
    }

    private static HttpClient CreateDownloadClient()
    {
        // No overall timeout: a model is gigabytes, and a stalled connection surfaces as a read error anyway
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{ApplicationInfo.CommandName}/{ApplicationInfo.Version}");

        return client;
    }
}