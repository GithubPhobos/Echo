using System.Runtime.Versioning;
using NAudio.Wave;

namespace Echo.Platform.Linux;

/// <summary>
///     The loudspeakers of Linux, through the sound server.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class PulseSoundOutput : ISoundOutput
{
    /// <inheritdoc/>
    public IDisposable Play(ISampleProvider source) =>
        new Playback(source);

    /// <summary>
    ///     A stream that keeps feeding the server whatever the source gives, silence included.
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

        private readonly ISampleProvider _source;
        private readonly IntPtr _stream;
        private readonly Thread _writer;
        private volatile bool _isStopping;

        public Playback(ISampleProvider source)
        {
            _source = source;

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

            _stream = PulseAudioNative.SimpleNew(
                null, ApplicationInfo.CommandName, PulseAudioNative.Playback, null, "Sounds", ref sampleSpec, IntPtr.Zero, ref attributes, out int error);

            if (_stream == IntPtr.Zero)
                throw new IOException($"The sound server could not open the loudspeakers: {PulseAudioNative.Describe(error)}.");

            _writer = new Thread(Write) { IsBackground = true, Name = "Sounds" };
            _writer.Start();
        }

        public void Dispose()
        {
            _isStopping = true;

            // A write returns once the server has room, which takes at most the latency above
            _writer.Join();

            PulseAudioNative.SimpleFree(_stream);
        }

        private void Write()
        {
            float[] samples = new float[_source.WaveFormat.SampleRate * _source.WaveFormat.Channels * PieceMilliseconds / 1000];
            byte[] bytes = new byte[samples.Length * sizeof(float)];

            while (!_isStopping)
            {
                int read = _source.Read(samples, 0, samples.Length);

                // Silence when the source has nothing: the server's own clock paces the loop either way
                Array.Clear(samples, read, samples.Length - read);
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

                if (PulseAudioNative.SimpleWrite(_stream, bytes, (UIntPtr)bytes.Length, out _) < 0)
                    return;
            }
        }
    }
}