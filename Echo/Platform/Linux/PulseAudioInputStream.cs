using System.Runtime.Versioning;

namespace Echo.Platform.Linux;

/// <inheritdoc cref="IAudioInputStream"/>
/// <remarks>
///     The reading thread owns the stream once it runs: it frees the stream when it ends, so the stream is never
///     freed under a read, and is freed also when the server answers only after closing stopped waiting for it.
/// </remarks>
/// <param name="device">The name of the device for the sound server, or null for its default microphone.</param>
/// <param name="bufferMilliseconds">How much sound each piece holds.</param>
/// <param name="pulse">The sound server; tests give one of their own.</param>
/// <param name="stopTimeout">How long closing waits for the server; tests give a short time.</param>
[SupportedOSPlatform("linux")]
internal sealed class PulseAudioInputStream(string? device, int bufferMilliseconds, IPulseSimple? pulse = null, TimeSpan? stopTimeout = null)
    : IAudioInputStream
{
    /// <summary>
    ///     The format Whisper takes; the sound server converts whatever the microphone gives into it.
    /// </summary>
    private const uint SampleRate = 16000;
    private const int BytesPerSample = 2;

    /// <summary>
    ///     How long closing waits for the reading thread, which needs at most one piece.
    /// </summary>
    private static readonly TimeSpan _defaultStopTimeout = TimeSpan.FromSeconds(2);

    private readonly IPulseSimple _pulse = pulse ?? new PulseSimple();
    private readonly TimeSpan _stopTimeout = stopTimeout ?? _defaultStopTimeout;
    private readonly string? _device = device;
    private readonly int _pieceBytes = (int)(SampleRate * BytesPerSample * Math.Max(10, bufferMilliseconds) / 1000);

    private IntPtr _stream;
    private Thread? _reader;
    private volatile bool _isStopping;

    /// <inheritdoc/>
    public event EventHandler<AudioInputEventArgs>? DataAvailable;

    /// <summary>
    ///     Opens the microphone at once, so a device that cannot be opened is reported to the caller,
    ///     and then reads it on a thread of its own.
    /// </summary>
    public void Start()
    {
        var sampleSpec = new PulseAudioNative.SampleSpec
        {
            Format = PulseAudioNative.Signed16,
            Rate = SampleRate,
            Channels = 1
        };

        // The server sends a piece as soon as it holds this much, instead of the two seconds it would gather otherwise
        var attributes = new PulseAudioNative.BufferAttributes
        {
            MaxLength = PulseAudioNative.Invalid,
            TargetLength = PulseAudioNative.Invalid,
            Prebuffer = PulseAudioNative.Invalid,
            MinimumRequest = PulseAudioNative.Invalid,
            FragmentSize = (uint)_pieceBytes
        };

        _stream = _pulse.Open(PulseAudioNative.Record, _device, "Dictation", sampleSpec, attributes, out int error);

        if (_stream == IntPtr.Zero)
            throw new IOException($"The sound server could not open the microphone: {_pulse.Describe(error)}.");

        _reader = new Thread(Read) { IsBackground = true, Name = "Microphone" };
        _reader.Start();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _isStopping = true;

        // A read ends with the piece it waits for, so the thread is gone within one piece. A server that stopped
        // answering keeps the thread, which frees the stream once the read returns
        _reader?.Join(_stopTimeout);
    }

    private void Read()
    {
        try
        {
            while (!_isStopping)
            {
                byte[] piece = new byte[_pieceBytes];

                if (_pulse.Read(_stream, piece, out _) < 0)
                    return;

                if (!_isStopping)
                    DataAvailable?.Invoke(this, new AudioInputEventArgs(piece, piece.Length));
            }
        }
        finally
        {
            _pulse.Free(_stream);
        }
    }
}