namespace Echo.Platform.Contracts;

/// <summary>
///     The simple API of the Linux sound server (pa_simple): a stream that is opened, read or written piece by
///     piece, and freed. Reading and writing wait for the server, for as long as it takes.
/// </summary>
internal interface IPulseSimple
{
    /// <summary>
    ///     Opens a stream. Returns zero, with the server's error, when it cannot be opened.
    /// </summary>
    /// <param name="direction"><see cref="PulseAudioNative.Playback"/> or <see cref="PulseAudioNative.Record"/>.</param>
    /// <param name="device">The device, or null for the server's default one.</param>
    /// <param name="streamName">What the stream is called in the server's list of streams.</param>
    /// <param name="sampleSpec">The format of the sound.</param>
    /// <param name="attributes">How the server buffers the stream.</param>
    /// <param name="error">The server's error when the stream could not be opened.</param>
    IntPtr Open(int direction,
                string? device,
                string streamName,
                PulseAudioNative.SampleSpec sampleSpec,
                PulseAudioNative.BufferAttributes attributes,
                out int error);

    /// <summary>
    ///     Fills the whole piece with sound from the stream. Negative when the server failed.
    /// </summary>
    int Read(IntPtr stream, byte[] piece, out int error);

    /// <summary>
    ///     Hands the whole piece of sound to the stream. Negative when the server failed.
    /// </summary>
    int Write(IntPtr stream, byte[] piece, out int error);

    /// <summary>
    ///     Closes the stream. Never while a read or a write on it is under way.
    /// </summary>
    void Free(IntPtr stream);

    /// <summary>
    ///     The server's words for an error.
    /// </summary>
    string Describe(int error);
}