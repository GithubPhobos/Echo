namespace Echo.Platform.Windows;

/// <summary>
///     The microphones of Windows, through the classic recording interface of the system.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WaveInAudioInput : IAudioInput
{
    /// <inheritdoc/>
    public IReadOnlyList<MicrophoneDevice> GetDevices() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount)
            .Select(index => new MicrophoneDevice(index, WaveInEvent.GetCapabilities(index).ProductName))
            .ToList();

    /// <inheritdoc/>
    public IAudioInputStream Open(int deviceIndex, int bufferMilliseconds, int numberOfBuffers) =>
        new WaveInAudioInputStream(deviceIndex, bufferMilliseconds, numberOfBuffers);
}