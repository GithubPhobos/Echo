namespace Echo.Platform.Contracts;

/// <summary>
///     An open microphone that sends what it hears piece by piece, for as long as it is open.
///     Closing it waits until the device has really stopped, so it can be opened again at once.
/// </summary>
internal interface IAudioInputStream : IDisposable
{
    /// <summary>
    ///     Raised for every piece of sound, on a thread of the audio system.
    /// </summary>
    event EventHandler<AudioInputEventArgs>? DataAvailable;

    /// <summary>
    ///     Starts sending sound.
    /// </summary>
    void Start();
}