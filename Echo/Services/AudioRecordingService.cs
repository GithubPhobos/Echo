using Echo.Setup.Audio;
using NAudio.Utils;
using NAudio.Wave;

namespace Echo.Services;

/// <inheritdoc cref="IAudioRecordingService"/>
/// <param name="audioInput">The microphones of the system.</param>
/// <param name="logger">The logger instance.</param>
/// <param name="recordingDeviceSettingsOptions">The audio device configuration settings.</param>
/// <param name="activity">The working screen, when Echo runs with one: it shows the level of the microphone.</param>
internal sealed class AudioRecordingService(
    IAudioInput audioInput,
    ILogger<AudioRecordingService> logger,
    IOptions<RecordingDeviceSettings> recordingDeviceSettingsOptions,
    IListenActivity? activity = null) : IAudioRecordingService
{
    private readonly IListenActivity? _activity = activity;

    /// <summary>
    ///     Required audio format for Whisper inference.
    ///     Strictly 16kHz, 16-bit, Mono.
    /// </summary>
    private static readonly WaveFormat _whisperFormat = new(16000, 16, 1);

    private readonly IAudioInput _audioInput = audioInput;
    private readonly ILogger<AudioRecordingService> _logger = logger;
    private readonly RecordingDeviceSettings _recordingDeviceSettings = recordingDeviceSettingsOptions.Value;

    /// <summary>
    ///     Synchronization lock object to prevent race conditions between the main UI/Hook thread
    ///     and the background audio thread.
    /// </summary>
    private readonly object _lockObj = new();

    /// <summary>
    ///     Guards opening and closing the microphone. Apart from <see cref="_lockObj"/>, since closing waits for
    ///     the audio thread, which may itself be waiting for that one.
    /// </summary>
    private readonly object _deviceLock = new();

    private IAudioInputStream? _microphone;

    /// <summary>
    ///     Whether the microphone is meant to be open: from the start-up until the service is closed.
    ///     Only then is a microphone that the system closed opened again.
    /// </summary>
    private bool _keepsMicrophoneOpen;

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

        IReadOnlyList<MicrophoneDevice> devices = _audioInput.GetDevices();

        int deviceCount = devices.Count;
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

        int? chosen = FindChosenDevice(devices);

        foreach (MicrophoneDevice device in devices)
        {
            _logger.LogDebug("{Tab}[{i}]: {ProductName}{InUse}",
                LoggerConstants.Tab,
                device.Index,
                device.Name,
                device.Index == chosen ? " (in use)" : string.Empty);
        }

        // The devices can change between two starts: the one chosen in setup may be gone
        if (chosen is not int deviceIndex)
        {
            throw new Exception(
                $"{LoggerConstants.Tab}{LoggerConstants.WarningEmoji} " +
                (_recordingDeviceSettings.DeviceName is { Length: > 0 } name
                    ? $"The microphone chosen in setup ({name}) is not connected. Connect it, or choose another one with /setup."
                    : $"The microphone chosen in setup ([{_recordingDeviceSettings.DeviceIndex}]) is not in the list above. Choose another one with /setup."));
        }

        if (deviceIndex != _recordingDeviceSettings.DeviceIndex)
        {
            _logger.LogDebug("{Tab}{InfoEmoji} The microphone has moved in the list since setup: [{Saved}] is now [{Found}].",
                LoggerConstants.Tab, LoggerConstants.InfoEmoji, _recordingDeviceSettings.DeviceIndex, deviceIndex);
        }

        _logger.LogDebug("{Tab}{CheckedEmoji} Audio devices checked.",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji);

        lock (_deviceLock)
        {
            if (_microphone is not null) return;

            try
            {
                _microphone = Open(deviceIndex);
                _keepsMicrophoneOpen = true;
            }
            catch (Exception exc)
            {
                _logger.LogError(exc, "Failed to initialize microphone hardware.");
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public void ReopenMicrophone()
    {
        lock (_deviceLock)
        {
            if (!_keepsMicrophoneOpen) return;

            Close();
            TryOpen();
        }
    }

    /// <inheritdoc/>
    public void StartRecording()
    {
        if (_isPttActive) return;

        // The microphone the system closed may not have opened again right away: each recording tries once more
        lock (_deviceLock)
        {
            if (_keepsMicrophoneOpen && _microphone is null)
                TryOpen();
        }

        try
        {
            // Lock prevents OnRecording from writing into partially initialized streams
            lock (_lockObj)
            {
                _audioStream = new MemoryStream();

                // NAudio requires a WaveFileWriter to generate the correct WAV header (RIFF) in memory
                _writer = new WaveFileWriter(
                    new IgnoreDisposeStream(_audioStream),
                    _whisperFormat);

                _hasVoiceStarted = false;
                _isPttActive = true;
            }

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

        return resultStream;
    }

    /// <summary>
    ///     Handles continuous incoming audio chunks from the hardware background thread.
    /// </summary>
    /// <remarks>
    ///     Implements Voice Activity Detection (VAD) to trim initial silence.
    /// </remarks>
    /// <param name="sender">The source of the event.</param>
    /// <param name="e">A <see cref="AudioInputEventArgs"/> containing the raw audio buffer data.</param>
    private void OnRecording(object? sender, AudioInputEventArgs e)
    {
        // Fast-path bypass (lock-free) to discard audio chunks when PTT is inactive
        if (!_isPttActive)
            return;

        _activity?.ReportLevel(PeakOf(e.Buffer, e.BytesRecorded));

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
    ///     The microphone chosen in setup, found by its name: the order of the devices can change between two
    ///     starts. Of several devices with that name, the one at the saved place comes first. Settings without
    ///     a name take the saved place. Null when the microphone is not there.
    /// </summary>
    private int? FindChosenDevice(IReadOnlyList<MicrophoneDevice> devices)
    {
        int index = _recordingDeviceSettings.DeviceIndex;

        if (_recordingDeviceSettings.DeviceName is not { Length: > 0 } name)
            return devices.Any(device => device.Index == index) ? index : null;

        MicrophoneDevice? found = devices.FirstOrDefault(device => device.Index == index && device.Name == name)
            ?? devices.FirstOrDefault(device => device.Name == name);

        return found?.Index;
    }

    /// <summary>
    ///     Opens the microphone and starts it. It keeps running from then on, so that a recording starts at once.
    /// </summary>
    /// <param name="deviceIndex">The device, as <see cref="IAudioInput.GetDevices"/> counts them.</param>
    private IAudioInputStream Open(int deviceIndex)
    {
        IAudioInputStream microphone = _audioInput.Open(
            deviceIndex,
            _recordingDeviceSettings.BufferMs,
            _recordingDeviceSettings.NumberOfBuffers);

        microphone.DataAvailable += OnRecording;

        try
        {
            microphone.Start();
        }
        catch
        {
            microphone.DataAvailable -= OnRecording;
            microphone.Dispose();

            throw;
        }

        return microphone;
    }

    /// <summary>
    ///     Opens the microphone chosen in setup once more. It is looked for again, since the devices may have
    ///     changed while it was closed. One that does not open is left closed for the next recording to try.
    /// </summary>
    private void TryOpen()
    {
        string problem;

        try
        {
            if (FindChosenDevice(_audioInput.GetDevices()) is int deviceIndex)
            {
                _microphone = Open(deviceIndex);

                return;
            }

            problem = _recordingDeviceSettings.DeviceName is { Length: > 0 } name
                ? $"{name} is not connected."
                : $"[{_recordingDeviceSettings.DeviceIndex}] is not in the list of devices.";
        }
        catch (Exception exc)
        {
            problem = exc.Message;
        }

        _logger.LogWarning("{WarningEmoji} Could not open the microphone again, the next recording tries once more: {Problem}",
            LoggerConstants.WarningEmoji, problem);
    }

    /// <summary>
    ///     Closes the microphone. One that the system has already closed may fail to close again: it is let go all
    ///     the same, since a new one is what the caller is after.
    /// </summary>
    private void Close()
    {
        if (_microphone is null) return;

        _microphone.DataAvailable -= OnRecording;

        try
        {
            // Waits until the device has really stopped: the setup wizard opens it again right after this
            _microphone.Dispose();
        }
        catch (Exception exc)
        {
            _logger.LogDebug("{Tab}Closing the microphone failed: {ErrorMessage}",
                LoggerConstants.Tab, exc.Message);
        }

        _microphone = null;
    }

    /// <summary>
    ///     The loudest sample of a piece of 16-bit sound, between 0 and 1.
    /// </summary>
    private static float PeakOf(byte[] buffer, int length)
    {
        float peak = 0f;

        for (int i = 0; i + 1 < length; i += 2)
            peak = Math.Max(peak, Math.Abs((float)BitConverter.ToInt16(buffer, i) / short.MaxValue));

        return peak;
    }

    /// <summary>
    ///     Stops hardware listening and releases all allocated memory resources.
    ///     Should only be called when the application is shutting down.
    /// </summary>
    private void Cleanup()
    {
        _isPttActive = false;

        lock (_deviceLock)
        {
            _keepsMicrophoneOpen = false;
            Close();
        }

        _writer?.Dispose();
        _writer = null;

        _audioStream?.Dispose();
        _audioStream = null;
    }

    /// <inheritdoc/>
    public void Dispose() => Cleanup();
}