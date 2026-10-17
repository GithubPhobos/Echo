using NAudio.Wave;

namespace Echo.Setup.Audio;

/// <inheritdoc cref="IMicrophoneProbe"/>
internal sealed class MicrophoneProbe : IMicrophoneProbe
{
    /// <inheritdoc/>
    public IReadOnlyList<MicrophoneDevice> GetDevices() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount)
            .Select(index => new MicrophoneDevice(index, WaveInEvent.GetCapabilities(index).ProductName))
            .ToList();

    /// <inheritdoc/>
    public IMicrophoneSession Open(int deviceIndex) =>
        new Session(deviceIndex);

    private sealed class Session : IMicrophoneSession
    {
        /// <summary>
        ///     How long the device may take to stop recording before it is closed regardless.
        /// </summary>
        private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(2);

        private readonly WaveInEvent _waveIn;
        private readonly ManualResetEventSlim _stopped = new();
        private float _peak;

        public Session(int deviceIndex)
        {
            // The same format the dictation itself records in, so the measured levels are comparable
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50
            };

            _waveIn.DataAvailable += OnData;
            _waveIn.RecordingStopped += OnStopped;
            _waveIn.StartRecording();
        }

        public float ReadPeak() =>
            Interlocked.Exchange(ref _peak, 0f);

        public void Dispose()
        {
            _waveIn.DataAvailable -= OnData;
            _waveIn.StopRecording();

            // Closing a device that is still winding down leaves it busy, and whoever opens it next
            // (the following page, or dictation itself) waits for it in vain
            _stopped.Wait(_stopTimeout);

            _waveIn.RecordingStopped -= OnStopped;
            _waveIn.Dispose();
            _stopped.Dispose();
        }

        private void OnStopped(object? sender, StoppedEventArgs e) =>
            _stopped.Set();

        private void OnData(object? sender, WaveInEventArgs e)
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