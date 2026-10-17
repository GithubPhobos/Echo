namespace Echo.Platform.Linux;

/// <summary>
///     The part of Linux that draws windows and hands them the keyboard.
/// </summary>
internal enum DisplayServer
{
    /// <summary>
    ///     No windows at all, such as a text console or a remote shell.
    /// </summary>
    None,

    /// <summary>
    ///     The older one: any program may hear the whole keyboard and press keys for the user.
    /// </summary>
    X11,

    /// <summary>
    ///     The newer one: a program asks the desktop for that through its permission windows (portals).
    /// </summary>
    Wayland
}