namespace Echo.Platform.Contracts;

/// <summary>
///     The microphones of the system. Everything is recorded in the format Whisper takes:
///     16 kHz, 16-bit, mono.
/// </summary>
internal interface IAudioInput
{
    /// <summary>
    ///     The recording devices, in the order the 'DeviceIndex' setting counts them.
    /// </summary>
    IReadOnlyList<MicrophoneDevice> GetDevices();

    /// <summary>
    ///     Opens a microphone. It sends nothing until <see cref="IAudioInputStream.Start"/>,
    ///     so the caller can subscribe first.
    /// </summary>
    /// <param name="deviceIndex">The device, as <see cref="GetDevices"/> counts them.</param>
    /// <param name="bufferMilliseconds">How much sound each piece holds.</param>
    /// <param name="numberOfBuffers">How many pieces the driver keeps ready, where it lets itself be told.</param>
    IAudioInputStream Open(int deviceIndex, int bufferMilliseconds, int numberOfBuffers);
}