namespace Echo.Setup.Audio;

/// <inheritdoc cref="IMicrophoneProbe"/>
/// <param name="audioInput">The microphones of the system.</param>
internal sealed class MicrophoneProbe(IAudioInput audioInput) : IMicrophoneProbe
{
    /// <summary>
    ///     Short pieces, so the level meter follows the voice closely.
    /// </summary>
    private const int BufferMilliseconds = 50;

    private const int NumberOfBuffers = 3;

    private readonly IAudioInput _audioInput = audioInput;

    /// <inheritdoc/>
    public IReadOnlyList<MicrophoneDevice> GetDevices() =>
        _audioInput.GetDevices();

    /// <inheritdoc/>
    public IMicrophoneSession Open(int deviceIndex) =>
        new Session(_audioInput.Open(deviceIndex, BufferMilliseconds, NumberOfBuffers));

    /// <summary>
    ///     The same format the dictation itself records in, so the measured levels are comparable.
    /// </summary>
    private sealed class Session : IMicrophoneSession
    {
        private readonly IAudioInputStream _microphone;
        private float _peak;

        public Session(IAudioInputStream microphone)
        {
            _microphone = microphone;

            _microphone.DataAvailable += OnData;
            _microphone.Start();
        }

        public float ReadPeak() =>
            Interlocked.Exchange(ref _peak, 0f);

        public void Dispose()
        {
            _microphone.DataAvailable -= OnData;

            // Closing waits for the device to stop: whoever opens it next (the following page,
            // or dictation itself) would otherwise wait for it in vain
            _microphone.Dispose();
        }

        private void OnData(object? sender, AudioInputEventArgs e)
        {
            float peak = 0f;

            for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(e.Buffer, i) / (float)short.MaxValue));

            // Only ever raises the stored peak; reading it resets it
            float current;
            do
            {
                current = _peak;
            }
            while (peak > current && Interlocked.CompareExchange(ref _peak, peak, current) != current);
        }
    }
}