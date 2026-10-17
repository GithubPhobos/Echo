using System.Runtime.Versioning;

namespace Echo.Platform.Linux;

/// <inheritdoc cref="IAudioInputStream"/>
/// <param name="device">The name of the device for the sound server, or null for its default microphone.</param>
/// <param name="bufferMilliseconds">How much sound each piece holds.</param>
[SupportedOSPlatform("linux")]
internal sealed class PulseAudioInputStream(string? device, int bufferMilliseconds) : IAudioInputStream
{
    /// <summary>
    ///     The format Whisper takes; the sound server converts whatever the microphone gives into it.
    /// </summary>
    private const uint SampleRate = 16000;
    private const int BytesPerSample = 2;

    /// <summary>
    ///     How long closing waits for the reading thread, which needs at most one piece.
    /// </summary>
    private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(2);

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

        _stream = PulseAudioNative.SimpleNew(
            null, ApplicationInfo.CommandName, PulseAudioNative.Record, _device, "Dictation", ref sampleSpec, IntPtr.Zero, ref attributes, out int error);

        if (_stream == IntPtr.Zero)
            throw new IOException($"The sound server could not open the microphone: {PulseAudioNative.Describe(error)}.");

        _reader = new Thread(Read) { IsBackground = true, Name = "Microphone" };
        _reader.Start();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _isStopping = true;

        // A read ends with the piece it waits for, so the thread is gone within one piece. A server that stopped
        // answering keeps the thread; the stream is then left to the end of the process rather than freed under it
        if (_reader is not null && !_reader.Join(_stopTimeout))
            return;

        if (_stream != IntPtr.Zero)
        {
            PulseAudioNative.SimpleFree(_stream);
            _stream = IntPtr.Zero;
        }
    }

    private void Read()
    {
        while (!_isStopping)
        {
            byte[] piece = new byte[_pieceBytes];

            if (PulseAudioNative.SimpleRead(_stream, piece, (UIntPtr)piece.Length, out _) < 0)
                return;

            if (!_isStopping)
                DataAvailable?.Invoke(this, new AudioInputEventArgs(piece, piece.Length));
        }
    }
}