using System.Reflection;
using Microsoft.Extensions.Hosting;

namespace Echo.Services.BackgroundServices;

/// <summary>
///     The central background service that is responsible for bootstrapping hardware initialization, loading AI models, 
///     and orchestrating the flow between voice capture and text inference.
/// </summary>
/// <param name="pushToTalkMonitorService"><see cref="AssetsProvider"/></param>
/// <param name="logger"><see cref="ILogger{EchoOrchestrator}"/></param>
/// <param name="audioRecordingService"><see cref="SoundPlayService"/></param>
/// <param name="assetsProvider"><see cref="TextInsertionService"/></param>
/// <param name="soundPlayService"><see cref="AudioRecordingService"/></param>
/// <param name="textInsertionService"><see cref="WhisperInferenceService"/></param>
/// <param name="whisperInferenceService"><see cref="PushToTalkMonitorService"/></param>
internal sealed class EchoOrchestrator(
    IAssetsProvider assetsProvider,
    ILogger<EchoOrchestrator> logger,
    ISoundPlayService soundPlayService,
    ITextInsertionService textInsertionService,
    IAudioRecordingService audioRecordingService,
    IWhisperInferenceService whisperInferenceService,
    IPushToTalkMonitorService pushToTalkMonitorService) : BackgroundService
{
    private readonly ILogger<EchoOrchestrator> _logger = logger;
    private readonly IAssetsProvider _assetsProvider = assetsProvider;
    private readonly ISoundPlayService _soundPlayService = soundPlayService;
    private readonly ITextInsertionService _textInsertionService = textInsertionService;
    private readonly IAudioRecordingService _audioRecordingService = audioRecordingService;
    private readonly IWhisperInferenceService _whisperInferenceService = whisperInferenceService;
    private readonly IPushToTalkMonitorService _pushToTalkMonitorService = pushToTalkMonitorService;

    /// <inheritdoc/>
    /// <param name="stoppingToken"><see cref="CancellationToken"/></param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Log.Warning(
                "...................Ech🦻 (v.{Version}) is checking dependencies................",
                Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));

            _assetsProvider.InitializeAssetsDirectory();

            _audioRecordingService.InititalizeAudioRecordingCapabilities();

            _whisperInferenceService.TryInitializeWhisperModel();

            _pushToTalkMonitorService.InitializePushToTalkHotkeysOrThrow();

            _soundPlayService.EnsureCanPlaySounds();

            _pushToTalkMonitorService.OnRecordingTriggered += HandlePushToTalkTriggered;
            _pushToTalkMonitorService.OnRecordingReleased += HandlePushToTalkReleased;

            await _pushToTalkMonitorService.StartListeningAsync();

            Log.Warning("...................Ech🦻 is ready and running....................");
        }
        catch (Exception exc)
        {
            _logger.LogError("{ErrorMessage}", exc.Message);

            MessageBoxService.ShowErrorMessage(exc.Message);

            Environment.Exit(1);
        }
    }

    private void HandlePushToTalkTriggered(object? sender, EventArgs e)
    {
        _logger.LogInformation("{BlackSquareButtonEmoji} Push-to-talk pressed.", 
            LoggerConstants.BlackSquareButtonEmoji);

        _audioRecordingService.StartRecording();
    }

    private void HandlePushToTalkReleased(object? sender, EventArgs e)
    {
        _logger.LogInformation("{WhiteSquareButtonEmoji} Push-to-talk released.",
            LoggerConstants.WhiteSquareButtonEmoji);

        Task.Run(async () =>
        {
            try
            {
                MemoryStream? audioStream = _audioRecordingService.StopRecording();

                string? textFromAudio = await _whisperInferenceService.ProcessAudioAsync(audioStream);

                await _textInsertionService.InsertTextAsync(textFromAudio);
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "{ExplosionEmoji} Error: {ErrorMessage}", 
                    LoggerConstants.ExplosionEmoji, exc.Message);
            }
        });
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _pushToTalkMonitorService.OnRecordingTriggered -= HandlePushToTalkTriggered;
        _pushToTalkMonitorService.OnRecordingReleased -= HandlePushToTalkReleased;

        base.Dispose();
    }
}