using Echo.Services.BackgroundServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Sinks.SystemConsole.Themes;

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
    internal static void ConfigureSerilog(this IServiceCollection services, IConfiguration configuration)
    {
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Error)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}",
                theme: AnsiConsoleTheme.Code)
            .CreateLogger();

        services.AddSerilog();
    }

    /// <summary>
    ///     Builds the application configuration from 'appsettings.json' and binds the sections 
    ///     to strongly-typed options classes.
    /// </summary>
    /// <param name="services"><see cref="IServiceCollection"/></param>
    internal static void SetupConfiguration(this IServiceCollection services)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

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

        services.AddSingleton<IAssetsProvider, AssetsProvider>();
        services.AddSingleton<ISoundPlayService, SoundPlayService>();
        services.AddSingleton<ITextInsertionService, TextInsertionService>();
        services.AddSingleton<IAudioRecordingService, AudioRecordingService>();
        services.AddSingleton<IWhisperInferenceService, WhisperInferenceService>();
        services.AddSingleton<IPushToTalkMonitorService, PushToTalkMonitorService>();
    }
}