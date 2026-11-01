namespace Echo.Platform.Linux;

/// <summary>
///     The loudspeakers of Linux, through the sound server.
/// </summary>
/// <param name="pulse">The sound server; tests give one of their own.</param>
/// <param name="stopTimeout">How long closing waits for the server; tests give a short time.</param>
[SupportedOSPlatform("linux")]
internal sealed class PulseSoundOutput(IPulseSimple? pulse = null, TimeSpan? stopTimeout = null) : ISoundOutput
{
    /// <summary>
    ///     How long closing waits for the writing thread, which normally needs only the latency of the stream.
    /// </summary>
    private static readonly TimeSpan _defaultStopTimeout = TimeSpan.FromSeconds(2);

    private readonly IPulseSimple _pulse = pulse ?? new PulseSimple();
    private readonly TimeSpan _stopTimeout = stopTimeout ?? _defaultStopTimeout;

    /// <inheritdoc/>
    public IDisposable Play(ISampleProvider source) =>
        new Playback(_pulse, source, _stopTimeout);

    /// <summary>
    ///     A stream that keeps feeding the server whatever the source gives, silence included. The writing
    ///     thread owns the stream: it frees it when it ends, so the stream is never freed under a write.
    /// </summary>
    private sealed class Playback : IDisposable
    {
        /// <summary>
        ///     How far ahead the server is fed: short enough for a sound to follow the key at once.
        /// </summary>
        private const int LatencyMilliseconds = 50;

        /// <summary>
        ///     How much is handed over at a time.
        /// </summary>
        private const int PieceMilliseconds = 10;

        private readonly IPulseSimple _pulse;
        private readonly ISampleProvider _source;
        private readonly TimeSpan _stopTimeout;
        private readonly IntPtr _stream;
        private readonly Thread _writer;
        private volatile bool _isStopping;

        public Playback(IPulseSimple pulse, ISampleProvider source, TimeSpan stopTimeout)
        {
            _pulse = pulse;
            _source = source;
            _stopTimeout = stopTimeout;

            int bytesPerSecond = source.WaveFormat.SampleRate * source.WaveFormat.Channels * sizeof(float);

            var sampleSpec = new PulseAudioNative.SampleSpec
            {
                Format = PulseAudioNative.Float32,
                Rate = (uint)source.WaveFormat.SampleRate,
                Channels = (byte)source.WaveFormat.Channels
            };

            var attributes = new PulseAudioNative.BufferAttributes
            {
                MaxLength = PulseAudioNative.Invalid,
                TargetLength = (uint)(bytesPerSecond * LatencyMilliseconds / 1000),
                Prebuffer = PulseAudioNative.Invalid,
                MinimumRequest = PulseAudioNative.Invalid,
                FragmentSize = PulseAudioNative.Invalid
            };

            _stream = _pulse.Open(PulseAudioNative.Playback, null, "Sounds", sampleSpec, attributes, out int error);

            if (_stream == IntPtr.Zero)
                throw new IOException($"The sound server could not open the loudspeakers: {_pulse.Describe(error)}.");

            _writer = new Thread(Write) { IsBackground = true, Name = "Sounds" };
            _writer.Start();
        }

        public void Dispose()
        {
            _isStopping = true;

            // A write returns once the server has room, normally within the latency above. A server that stopped
            // answering keeps the thread: Echo goes on closing, and the thread frees the stream when the write returns
            _writer.Join(_stopTimeout);
        }

        private void Write()
        {
            float[] samples = new float[_source.WaveFormat.SampleRate * _source.WaveFormat.Channels * PieceMilliseconds / 1000];
            byte[] bytes = new byte[samples.Length * sizeof(float)];

            try
            {
                while (!_isStopping)
                {
                    int read = _source.Read(samples, 0, samples.Length);

                    // Silence when the source has nothing: the server's own clock paces the loop either way
                    Array.Clear(samples, read, samples.Length - read);
                    Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

                    if (_pulse.Write(_stream, bytes, out _) < 0)
                        return;
                }
            }
            finally
            {
                _pulse.Free(_stream);
            }
        }
    }
}