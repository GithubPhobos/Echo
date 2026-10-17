namespace Echo.Platform.Contracts;

/// <summary>
///     Tells whether the user is looking at the terminal the application runs in. Screens that listen
///     to the keyboard of the whole system use it to leave out what is typed into other windows.
/// </summary>
internal interface ITerminalFocus
{
    /// <summary>
    ///     Whether the terminal is the window in front, or null when that cannot be found out.
    ///     What to do then is up to the caller: the hotkey screen still hears the key, while a dictated
    ///     phrase is still typed into the window in front.
    /// </summary>
    bool? IsFocused();
}