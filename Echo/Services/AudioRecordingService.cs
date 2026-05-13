using NAudio.Utils;
using NAudio.Wave;

namespace Echo.Services;

/// <inheritdoc cref="IAudioRecordingService"/>
/// <param name="logger">The logger instance.</param>
/// <param name="recordingDeviceSettingsOptions">The audio device configuration settings.</param>
internal sealed class AudioRecordingService(
    ILogger<AudioRecordingService> logger,
    IOptions<RecordingDeviceSettings> recordingDeviceSettingsOptions) : IAudioRecordingService
{
    /// <summary>
    ///     Required audio format for Whisper inference. 
    ///     Strictly 16kHz, 16-bit, Mono.
    /// </summary>
    private static readonly WaveFormat _whisperFormat = new(16000, 16, 1);

    private readonly ILogger<AudioRecordingService> _logger = logger;
    private readonly RecordingDeviceSettings _recordingDeviceSettings = recordingDeviceSettingsOptions.Value;

    /// <summary>
    ///     Synchronization lock object to prevent race conditions between the main UI/Hook thread 
    ///     and the background NAudio hardware thread.
    /// </summary>
    private readonly object _lockObj = new();

    private WaveInEvent? _waveIn;

    /// <summary>
    ///     Indicates whether the logical recording session (Push-To-Talk) is currently active.
    /// </summary>
    private bool _isPttActive;

    /// <summary>
    ///     Indicates whether the initial silence has been passed and actual speech has started.
    /// </summary>
    private bool _hasVoiceStarted;

    private WaveFileWriter? _writer;
    private MemoryStream? _audioStream;

    /// <inheritdoc/>
    public void InititalizeAudioRecordingCapabilities()
    {
        _logger.LogDebug("{AudioEmoji} Audio:", LoggerConstants.ControlKnobsEmoji);

        int deviceCount = WaveInEvent.DeviceCount;
        if (deviceCount <= 0)
        {
            throw new Exception(
                $"{LoggerConstants.Tab}{LoggerConstants.WarningEmoji}{LoggerConstants.StudioMicrophoneEmoji}{LoggerConstants.WarningEmoji} " +
                $"Couldn't find recording devices in the system, shutting down...");
        }

        _logger.LogDebug("{Tab}{LoadingEmoji} Analyzing audio devices...",
            LoggerConstants.Tab, LoggerConstants.LoadingEmoji);
        _logger.LogDebug("{Tab}Found {DeviceCount} recording devices:",
            LoggerConstants.Tab, deviceCount);

        for (int i = 0; i < deviceCount; i++)
        {
            WaveInCapabilities deviceInfo = WaveInEvent.GetCapabilities(i);

            _logger.LogDebug("{Tab}[{i}]: {ProductName} (Channels: {ChannelsCount})",
                LoggerConstants.Tab,
                i,
                deviceInfo.ProductName,
                deviceInfo.Channels);
        }

        if (_recordingDeviceSettings.DeviceIndex == -1
            ||
            _recordingDeviceSettings.DeviceIndex > deviceCount + 1)
        {
            throw new Exception(
                $"{LoggerConstants.Tab}{LoggerConstants.WarningEmoji} " +
                $"Invalid selected device index ({_recordingDeviceSettings.DeviceIndex}). " +
                "Select correct device index from the list above.");
        }

        _logger.LogDebug("{Tab}{CheckedEmoji} Successfuly analized audio devices.",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji);

        InitializeMic();

        // Local function to handle actual hardware initialization
        void InitializeMic()
        {
            if (_waveIn is not null) return;

            try
            {
                _waveIn = new WaveInEvent
                {
                    WaveFormat = _whisperFormat,
                    DeviceNumber = _recordingDeviceSettings.DeviceIndex,
                    BufferMilliseconds = _recordingDeviceSettings.BufferMs,
                    NumberOfBuffers = _recordingDeviceSettings.NumberOfBuffers
                };

                _waveIn.DataAvailable += OnRecording;

                // Hardware is started once and kept running (Hot Mic pattern)
                _waveIn.StartRecording();
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "Failed to initialize microphone hardware.");
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public void StartRecording()
    {
        if (_isPttActive) return;

        try
        {
            // Lock prevents OnRecording from writing into partially initialized streams
            lock (_lockObj)
            {
                _audioStream = new MemoryStream();

                // NAudio requires a WaveFileWriter to generate the correct WAV header (RIFF) in memory
                _writer = new WaveFileWriter(
                    new IgnoreDisposeStream(_audioStream),
                    _waveIn!.WaveFormat);

                _hasVoiceStarted = false;
                _isPttActive = true;
            }

            _logger.LogInformation("{Tab}{MicrophoneOnEmoji} Microphone activated, recording audio...",
                LoggerConstants.Tab, LoggerConstants.MicrophoneOnEmoji);
        }
        catch (Exception exc)
        {
            _logger.LogError(exc, "{WarningEmoji} Failed to start audio recording: {ErrorMessage}.",
                LoggerConstants.WarningEmoji, exc.Message);

            // In case of failure, clear session state but KEEP hardware running
            lock (_lockObj)
            {
                _isPttActive = false;
                _writer?.Dispose();
                _writer = null;
                _audioStream?.Dispose();
                _audioStream = null;
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public MemoryStream? StopRecording()
    {
        if (!_isPttActive)
            return null;

        MemoryStream? resultStream = null;

        // Lock prevents OnRecording from writing while streams are being disposed
        lock (_lockObj)
        {
            _isPttActive = false;

            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;

            if (_audioStream is not null)
            {
                _audioStream.Position = 0; // Rewind the stream
                resultStream = _audioStream;
                _audioStream = null; // Clear internal reference to prevent memory leaks
            }
        }

        _logger.LogInformation("{Tab}{MicrophoneOffEmoji} Microphone deactivated.",
            LoggerConstants.Tab, LoggerConstants.MicrophoneOffEmoji);

        return resultStream;
    }

    /// <summary>
    ///     Handles continuous incoming audio chunks from the hardware background thread.
    /// </summary>
    /// <remarks>
    ///     Implements Voice Activity Detection (VAD) to trim initial silence.
    /// </remarks>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">A <see cref="WaveInEventArgs"/> containing the raw audio buffer data.</param>
    private void OnRecording(object? sender, WaveInEventArgs e)
    {
        // Fast-path bypass (lock-free) to discard audio chunks when PTT is inactive
        if (!_isPttActive)
            return;

        lock (_lockObj)
        {
            // Double-check inside lock to ensure StopRecording hasn't disposed the writer
            if (!_isPttActive || _writer is null) return;

            // If threshold is disabled, or speech has already begun, bypass math and write directly
            if (_recordingDeviceSettings.SilenceThreshold is null || _hasVoiceStarted)
            {
                _writer.Write(e.Buffer, 0, e.BytesRecorded);
                return;
            }

            // Voice Activity Detection (Silence Trimming)
            float maxAmplitude = 0f;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short sample = BitConverter.ToInt16(e.Buffer, i);
                float normalizedVolume = Math.Abs((float)sample / short.MaxValue);

                if (normalizedVolume > maxAmplitude)
                {
                    maxAmplitude = normalizedVolume;
                }
            }

            if (!_hasVoiceStarted && _recordingDeviceSettings.OutputMicAmplitudeDebugInfo)
            {
                _logger.LogDebug("{Tab}{InfoEmoji} Current mic amplitude: {Amplitude:F4}", 
                    LoggerConstants.Tab, LoggerConstants.InfoEmoji, maxAmplitude);
            }

            // Once the amplitude exceeds the threshold, unlock the stream for all subsequent chunks
            if (maxAmplitude > _recordingDeviceSettings.SilenceThreshold.Value)
            {
                _hasVoiceStarted = true;
                _writer.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }
    }

    /// <summary>
    ///     Stops hardware listening and releases all allocated memory resources.
    ///     Should only be called when the application is shutting down.
    /// </summary>
    private void Cleanup()
    {
        _isPttActive = false;

        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnRecording;
            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveIn = null;
        }

        _writer?.Dispose();
        _writer = null;

        _audioStream?.Dispose();
        _audioStream = null;
    }

    /// <inheritdoc/>
    public void Dispose() => Cleanup();
}