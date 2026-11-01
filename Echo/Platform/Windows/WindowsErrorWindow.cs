namespace Echo.Platform.Windows;

/// <summary>
///     Provides a wrapper around the native Windows API to display modal message boxes.
/// </summary>
/// <remarks>
///     Is used to show fatal errors before the application host fully starts or when logging fails.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsErrorWindow : IErrorWindow
{
    /// <summary>
    ///     Displays a native Windows modal error dialog with a stop/error icon.
    /// </summary>
    /// <param name="message">The text message to display inside the dialog body.</param>
    public void Show(string message)
    {
        MessageBox(
            hWnd: IntPtr.Zero,
            text: message,
            caption: "Fatal error",
            type: 0x00000010); // Error icon
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}