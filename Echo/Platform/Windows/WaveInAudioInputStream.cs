using System.Runtime.Versioning;
using NAudio.Wave;

namespace Echo.Platform.Windows;

/// <inheritdoc cref="IAudioInputStream"/>
[SupportedOSPlatform("windows")]
internal sealed class WaveInAudioInputStream : IAudioInputStream
{
    /// <summary>
    ///     How long closing the microphone may take. Opening the same device again before it has really
    ///     stopped leaves the next opening hanging, which is what the setup wizard does right after this.
    /// </summary>
    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Required audio format for Whisper inference.
    ///     Strictly 16kHz, 16-bit, Mono.
    /// </summary>
    private static readonly WaveFormat _whisperFormat = new(16000, 16, 1);

    private readonly WaveInEvent _waveIn;
    private readonly ManualResetEventSlim _stopped = new();

    private bool _isStarted;

    /// <inheritdoc/>
    public event EventHandler<AudioInputEventArgs>? DataAvailable;

    /// <param name="deviceIndex">The device number of the system.</param>
    /// <param name="bufferMilliseconds">How much sound each piece holds.</param>
    /// <param name="numberOfBuffers">How many pieces the driver keeps ready.</param>
    public WaveInAudioInputStream(int deviceIndex, int bufferMilliseconds, int numberOfBuffers)
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = _whisperFormat,
            DeviceNumber = deviceIndex,
            BufferMilliseconds = bufferMilliseconds,
            NumberOfBuffers = numberOfBuffers
        };

        _waveIn.DataAvailable += OnData;
        _waveIn.RecordingStopped += OnStopped;
    }

    /// <inheritdoc/>
    public void Start()
    {
        _waveIn.StartRecording();
        _isStarted = true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _waveIn.DataAvailable -= OnData;

        if (_isStarted)
        {
            _waveIn.StopRecording();

            // Closing a device that is still winding down leaves it busy, and whoever opens it next
            // (the following page, or dictation itself) waits for it in vain
            _stopped.Wait(_stopTimeout);
        }

        _waveIn.RecordingStopped -= OnStopped;
        _waveIn.Dispose();
        _stopped.Dispose();
    }

    private void OnStopped(object? sender, StoppedEventArgs e) =>
        _stopped.Set();

    private void OnData(object? sender, WaveInEventArgs e) =>
        DataAvailable?.Invoke(this, new AudioInputEventArgs(e.Buffer, e.BytesRecorded));
}