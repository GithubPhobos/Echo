using Echo.Setup.Hardware;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace Echo.Services;

/// <inheritdoc cref="IWhisperInferenceService"/>
/// <param name="paths"><see cref="IAppPaths"/></param>
/// <param name="cudaLibraries">Finds the NVIDIA libraries the CUDA runtime needs, the way this system does.</param>
/// <param name="systemLibraries">Finds the libraries of the system recognition needs.</param>
/// <param name="vulkan">Whether the automatic setting may load Vulkan on this system.</param>
/// <param name="logger"><see cref="ILogger{WhisperInferenceService}"/></param>
/// <param name="whisperSettingsOptions"><see cref="IOptions{WhisperSettings}"/></param>
internal sealed class WhisperInferenceService(
    IAppPaths paths,
    ICudaLibraries cudaLibraries,
    ISystemLibraries systemLibraries,
    IVulkanSupport vulkan,
    ILogger<WhisperInferenceService> logger,
    IOptions<WhisperSettings> whisperSettingsOptions) : IWhisperInferenceService
{
    private readonly IAppPaths _paths = paths;
    private readonly ICudaLibraries _cudaLibraries = cudaLibraries;
    private readonly ISystemLibraries _systemLibraries = systemLibraries;
    private readonly IVulkanSupport _vulkan = vulkan;
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

            // Without them the recognition library does not fail, it takes the whole process down
            if (_systemLibraries.FindMissing() is { Count: > 0 } missingLibraries)
            {
                throw new Exception(
                    $"{LoggerConstants.Tab}{LoggerConstants.WarningEmoji} Speech recognition needs these libraries of the system: " +
                    $"{string.Join(", ", missingLibraries)}. Run '{ApplicationInfo.CommandName} {Commands.SetupCommand.Name}' to see how to install them.");
            }

            // The NVIDIA libraries live in the user's folder, not next to the executable
            _cudaLibraries.PrepareForLoading(_paths.CudaDirectory);

            // The card number in the settings follows the order of the driver tool, so CUDA must count the same way
            if (Environment.GetEnvironmentVariable(CudaRequirements.DeviceOrderVariable) is null)
                Environment.SetEnvironmentVariable(CudaRequirements.DeviceOrderVariable, CudaRequirements.DeviceOrderByBus);

            IReadOnlyList<string> missingCudaLibraries = _cudaLibraries.FindMissing(_paths.CudaDirectory);

            if (missingCudaLibraries.Count > 0 && Acceleration.Parse(_whisperSettings.HardwareBackend) == AccelerationBackend.Cuda)
            {
                _logger.LogWarning("{Tab}{WarningEmoji} CUDA is selected, but these NVIDIA libraries are missing: {Libraries}. Falling back to the processor.",
                    LoggerConstants.Tab, LoggerConstants.WarningEmoji, string.Join(", ", missingCudaLibraries));
            }

            RuntimeOptions.RuntimeLibraryOrder = Acceleration.LibraryOrder(
                _whisperSettings.HardwareBackend,
                isCudaInstalled: missingCudaLibraries.Count == 0,
                isVulkanChosenAutomatically: _vulkan.IsChosenAutomatically,
                RuntimeOptions.RuntimeLibraryOrder);

            _logger.LogDebug("{Tab}{InfoEmoji} Loading order: {Libraries}",
                LoggerConstants.Tab,
                LoggerConstants.InfoEmoji,
                RuntimeOptions.RuntimeLibraryOrder.Select(
                    lib => Enum.GetName(lib)));

            string modelPath = ResolveModelPath();

            LogProvider.AddLogger(OnWhisperLog);

            _whisperFactory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions
            {
                UseGpu = Acceleration.UsesGpu(_whisperSettings.HardwareBackend),
                GpuDevice = _whisperSettings.GpuDeviceIndex
            });

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

    /// <summary>
    ///     Finds the model file in the user's models folder.
    /// </summary>
    private string ResolveModelPath()
    {
        // Only the file name is used, so the setting cannot point outside the models folder
        string fileName = Path.GetFileName(NormalizeModelFileName(_whisperSettings.ModelName));
        string modelPath = Path.Combine(_paths.ModelsDirectory, fileName);

        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException(
                $"{LoggerConstants.Tab}{LoggerConstants.WarningEmoji} Model '{fileName}' was not found in '{_paths.ModelsDirectory}'. " +
                $"Run '{ApplicationInfo.CommandName} {Commands.SetupCommand.Name}' to download a model.");
        }

        return modelPath;
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
            // Lines such as "whisper_backend_init_gpu: ..." are for whoever looks into a problem: the screen dims them
            using IDisposable fromLibrary = Serilog.Context.LogContext.PushProperty(Echo.Listening.ScreenLogSink.LibraryProperty, true);

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

        // We use a 4KB buffer (the perfect balance between CPU and RAM)
        byte[] buffer = new byte[4096];
        int bytesRead;

        // Reading audio in large chunks
        while ((bytesRead = audioStream.Read(buffer, 0, buffer.Length)) > 0)
        {
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

    private void LogRecognitionResults(Stopwatch stopwatch, string finalResult)
    {
        // The working screen shows the recognized phrase in a line of its own; this line is for the file
        using IDisposable hidden = Serilog.Context.LogContext.PushProperty(Echo.Listening.ScreenLogSink.HiddenProperty, true);

        _logger.LogDebug("{TextEmoji} Recognized in {Seconds} s: {FinalResult}",
            LoggerConstants.TextEmoji,
            stopwatch.Elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            finalResult);
    }
}