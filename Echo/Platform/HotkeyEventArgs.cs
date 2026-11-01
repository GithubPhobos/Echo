namespace Echo.Platform;

/// <summary>
///     A press or a release of the push-to-talk key.
/// </summary>
internal sealed class HotkeyEventArgs : EventArgs
{
    /// <summary>
    ///     Whether Echo keeps the key from the other programs. A handler sets it to false to let the key
    ///     do what it does without Echo; systems that hand the key to Echo alone keep it regardless.
    /// </summary>
    public bool IsTaken { get; set; } = true;
}