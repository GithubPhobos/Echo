namespace Echo.Setup.Hotkeys.Contracts;

/// <summary>
///     Starts watching the keyboard.
/// </summary>
internal interface IHotkeyCaptureFactory
{
    IHotkeyCapture Start();
}