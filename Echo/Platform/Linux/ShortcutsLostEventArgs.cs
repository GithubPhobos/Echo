namespace Echo.Platform.Linux;

/// <summary>
///     Why the desktop stopped giving Echo its push-to-talk key.
/// </summary>
/// <param name="reason">Why, in words for the log.</param>
/// <param name="isServiceGone">
///     Whether the portal service itself went away, rather than the desktop closing the session of the key.
/// </param>
internal sealed class ShortcutsLostEventArgs(string reason, bool isServiceGone) : EventArgs
{
    /// <summary>
    ///     Why, in words for the log.
    /// </summary>
    public string Reason { get; } = reason;

    /// <summary>
    ///     Whether the portal service itself went away. GNOME then keeps the key for the session that went with it,
    ///     and even a new start of Echo does not get it back: logging in again does.
    /// </summary>
    public bool IsServiceGone { get; } = isServiceGone;
}