namespace Echo.Setup.Hotkeys.Contracts;

/// <summary>
///     Watches the keyboard and reports which key the user pressed.
/// </summary>
internal interface IHotkeyCapture : IDisposable
{
    /// <summary>
    ///     Returns the next pressed key, if any. Enter and Escape are never reported: they drive the wizard.
    /// </summary>
    bool TryRead(out HotkeyChoice choice);
}