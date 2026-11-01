namespace Echo.Platform.Contracts;

/// <summary>
///     The loudspeakers of the system.
/// </summary>
internal interface ISoundOutput
{
    /// <summary>
    ///     Starts playing <paramref name="source"/> and keeps reading it until the returned object is disposed.
    ///     The source is expected to give silence when there is nothing to play, so the device stays open
    ///     and a sound starts without delay.
    /// </summary>
    /// <param name="source">What to play, 44.1 kHz stereo floating point.</param>
    IDisposable Play(ISampleProvider source);
}