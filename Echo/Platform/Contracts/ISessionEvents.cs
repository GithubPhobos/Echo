namespace Echo.Platform.Contracts;

/// <summary>
///     Says when the user comes back to the session Echo runs in, after another account or a remote connection
///     had the computer. The system closes the microphones and the loudspeakers of a session the user leaves,
///     and they stay closed for the programs that had them open.
/// </summary>
internal interface ISessionEvents
{
    /// <summary>
    ///     The user is back in this session. Raised on a thread of the system, which is not to be kept waiting.
    /// </summary>
    event EventHandler? Returned;
}