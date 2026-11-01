namespace Echo.Platform.Windows;

/// <summary>
///     Tells from the console window of the process whether its terminal is in front.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTerminalFocus(TerminalFocusReports? reports = null) : ITerminalFocus
{
    /// <summary>
    ///     The window class of the stand-in console window a terminal application gives its programs.
    /// </summary>
    internal const string PseudoConsoleClass = "PseudoConsoleWindow";

    private const uint RootOwner = 3;
    private const int ClassNameChars = 256;

    /// <inheritdoc/>
    public bool? IsFocused()
    {
        if (reports?.IsFocused is bool focused)
            return focused;
        IntPtr console = GetConsoleWindow();

        if (console == IntPtr.Zero)
            return null;

        return Decide(
            console,
            GetAncestor(console, RootOwner),
            GetForegroundWindow(),
            IsWindowVisible(console),
            ClassOf(console));
    }

    /// <summary>
    ///     Decides from what the system says about the console window.
    /// </summary>
    /// <param name="console">The console window of the process, or zero when it has none.</param>
    /// <param name="owner">The window that owns it at the top of the chain; the console itself when nothing does.</param>
    /// <param name="foreground">The window in front.</param>
    /// <param name="isConsoleVisible">Whether the console window is shown.</param>
    /// <param name="consoleClass">The window class of the console window.</param>
    internal static bool? Decide(IntPtr console, IntPtr owner, IntPtr foreground, bool isConsoleVisible, string consoleClass)
    {
        if (console == IntPtr.Zero)
            return null;

        // A terminal application owns the stand-in console window of the programs it runs
        if (owner != IntPtr.Zero && owner != console)
            return foreground == owner;

        // A hidden or stand-in console that nothing owns: the real window belongs to a terminal
        // that does not say which one it is
        if (!isConsoleVisible || consoleClass == PseudoConsoleClass)
            return null;

        return foreground == console;
    }

    /// <summary>
    ///     Whether the console is drawn by a terminal application, such as Windows Terminal, rather than by the
    ///     classic console window. A terminal application gets its programs a stand-in console window, and
    ///     that is so even when it was started for them by Windows and has not announced itself to them.
    /// </summary>
    public static bool IsHostedByTerminal()
    {
        IntPtr console = GetConsoleWindow();

        return console != IntPtr.Zero && ClassOf(console) == PseudoConsoleClass;
    }

    private static string ClassOf(IntPtr window)
    {
        char[] name = new char[ClassNameChars];
        int length = GetClassName(window, name, name.Length);

        return new string(name, 0, Math.Max(0, length));
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, [Out] char[] name, int maxCount);
}