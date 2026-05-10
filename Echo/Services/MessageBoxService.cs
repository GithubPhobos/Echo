using System.Runtime.InteropServices;

namespace Echo.Services;

/// <summary>
///     Provides a wrapper around the native Windows API to display modal message boxes.
/// </summary>
/// <remarks>
///     Is used to show fatal errors before the application host fully starts or when logging fails.
/// </remarks>
internal static class MessageBoxService
{
    /// <summary>
    ///     Displays a native Windows modal error dialog with a stop/error icon.
    /// </summary>
    /// <param name="errorMessage">The text message to display inside the dialog body.</param>
    public static void ShowErrorMessage(string errorMessage)
    {
        MessageBox(
            hWnd: IntPtr.Zero,
            text: errorMessage,
            caption: "Fatal error",
            type: 0x00000010); // Error icon
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
