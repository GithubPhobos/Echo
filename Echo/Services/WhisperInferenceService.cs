using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace Echo.Services;

/// <inheritdoc cref="IWhisperInferenceService"/>
/// <param name="assetsProvider"><see cref="IAssetsProvider"/></param>
/// <param name="logger"><see cref="ILogger{WhisperInferenceService}"/></param>
/// <param name="whisperSettingsOptions"><see cref="IOptions{WhisperSettings}"/></param>
internal sealed class WhisperInferenceService(
    IAssetsProvider assetsProvider,
    ILogger<WhisperInferenceService> logger,
    IOptions<WhisperSettings> whisperSettingsOptions) : IWhisperInferenceService
{
    private readonly IAssetsProvider _assetsProvider = assetsProvider;
    private readonly ILogger<WhisperInferenceService> _logger = logger;
    private readonly WhisperSettings _whisperSettings = whisperSettingsOptions.Value;

    private WhisperProcessor? _processor;
    private WhisperFactory? _whisperFactory;

    /// <inheritdoc/>
    public void TryInitializeWhisperModel()
    {
        try
        {
            _logger.LogDebug(@"{RobotEmoji} ""Whisper"":", LoggerConstants.RobotEmoji);

            _logger.LogDebug("{Tab}{LoadingEmoji} Loading Whisper model...",
                LoggerConstants.Tab, LoggerConstants.LoadingEmoji);

            _logger.LogDebug("{Tab}{InfoEmoji} Loading order: {Libraries}",
                LoggerConstants.Tab,
                LoggerConstants.InfoEmoji,
                RuntimeOptions.RuntimeLibraryOrder.Select(
                    lib => Enum.GetName(lib)));

            string modelPath = _assetsProvider.GetFilePath(
                NormalizeModelFileName(_whisperSettings.ModelName));

            LogProvider.AddLogger(OnWhisperLog);

            _whisperFactory = WhisperFactory.FromPath(modelPath);

            _processor = BuildWhisperProcessor(_whisperFactory, _whisperSettings);

            _logger.LogDebug("{Tab}{CheckedEmoji} Whisper model loaded successfully.",
                LoggerConstants.Tab, LoggerConstants.CheckedEmoji);

            _logger.LogDebug("{Tab}{InfoEmoji} Loaded library: [{Library}].",
                LoggerConstants.Tab, LoggerConstants.InfoEmoji, RuntimeOptions.LoadedLibrary);
        }
        catch (Exception exc)
        {
            _logger.LogError(exc, "{Tab}{ExplosionEmoji} Failed to load Whisper model: {ErrorMessage}",
                LoggerConstants.Tab, LoggerConstants.ExplosionEmoji, exc.Message);

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> ProcessAudioAsync(MemoryStream? audioStream)
    {
        if (IsAudioSilentOrEmpty(audioStream))
        {
            _logger.LogWarning("{WarningEmoji} Audio was silent or empty!",
                LoggerConstants.WarningEmoji);

            return null;
        }

        _logger.LogDebug("{LoadingEmoji} Starting audio recognition...",
            LoggerConstants.LoadingEmoji);

        Stopwatch stopwatch = Stopwatch.StartNew();

        var sb = new StringBuilder();
        await foreach (SegmentData result in _processor!.ProcessAsync(audioStream!))
        {
            sb.Append(result.Text);
        }
        string finalText = sb.ToString().Trim();

        stopwatch.Stop();

        LogRecognitionResults(stopwatch, finalText);

        return finalText;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _processor?.Dispose();
        _whisperFactory?.Dispose();
    }

    private static WhisperProcessor BuildWhisperProcessor(WhisperFactory whisperFactory,
                                                          WhisperSettings whisperSettings)
    {
        WhisperProcessorBuilder whisperProcessorBuilder = whisperFactory.CreateBuilder();

        whisperProcessorBuilder.ApplySettings(whisperSettings);

        return
            whisperProcessorBuilder.Build();
    }

    private static string NormalizeModelFileName(string modelName)
    {
        const string BinExtension = ".bin";

        var extension = Path.GetExtension(modelName);
        if (string.IsNullOrWhiteSpace(extension))
            return $"{modelName}{BinExtension}";

        return modelName;
    }

    private void OnWhisperLog(WhisperLogLevel arg1, string? arg2)
    {
        if (arg2 is null)
            return;

        if (arg1 is WhisperLogLevel.Info or WhisperLogLevel.Error)
        {
            _logger.LogDebug("{Tab}{Message}", LoggerConstants.Tab, arg2.Trim());

            return;
        }

        if (arg1 is WhisperLogLevel.Warning)
        {
            _logger.LogWarning("{Tab}{WarningEmoji} {Message}",
                LoggerConstants.Tab, LoggerConstants.WarningEmoji, arg2.Trim());

            return;
        }
    }

    /// <summary>
    ///     Checks if the provided audio stream is empty, too short, or contains only background noise.
    /// </summary>
    /// <param name="audioStream">The memory stream containing the WAV audio data.</param>
    /// <returns>True if the audio is silent or invalid; otherwise, false.</returns>
    internal static bool IsAudioSilentOrEmpty(MemoryStream? audioStream)
    {
        // Null or completely empty stream check
        if (audioStream is null || audioStream.Length == 0)
            return true;

        // Length Filter: Discard audio that is too short.
        // Assuming 16kHz, 16-bit, Mono: 16,000 bytes equals exactly 0.5 seconds of audio.
        // This also automatically filters out streams that contain only the 44-byte WAV header.
        if (audioStream.Length < 16000)
        {
            return true;
        }

        // Amplitude Filter: Memory-optimized check for voice activity.
        int maxAmplitude = 0;

        // Skip the 44-byte standard WAV header to process only the raw PCM audio data.
        audioStream.Position = 44;

        // Используем буфер на 4KB (идеальный баланс между CPU и RAM)
        byte[] buffer = new byte[4096];
        int bytesRead;

        // Читаем аудио большими кусками
        while ((bytesRead = audioStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            // Шагаем внутри куска по 2 байта
            for (int i = 0; i < bytesRead - 1; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                int absSample = Math.Abs((int)sample);

                if (absSample > maxAmplitude)
                {
                    maxAmplitude = absSample;
                }
            }
        }

        audioStream.Position = 0;

        return
            maxAmplitude < 500;
    }

    private void LogRecognitionResults(Stopwatch stopwatch, string finalText)
    {
        _logger.LogDebug("{CheckedEmoji} Audio processed:", LoggerConstants.CheckedEmoji);

        _logger.LogDebug("{Tab}{RobotEmoji} Model used: {ModelName}.",
            LoggerConstants.Tab, LoggerConstants.RobotEmoji, _whisperSettings.ModelName);

        _logger.LogDebug("{Tab}{TimerEmoji} Recognition time: {Elapsed} ms.",
            LoggerConstants.Tab, LoggerConstants.TimerEmoji, stopwatch.ElapsedMilliseconds);

        _logger.LogDebug("{Tab}{TextEmoji} Финальный текст: {FinalText}",
            LoggerConstants.Tab, LoggerConstants.TextEmoji, finalText);

        _logger.LogDebug("========================================================================");
    }
}