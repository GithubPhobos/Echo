using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Echo.Infrastructure;

/// <summary>
///     Tells whether the user is looking at the terminal the application runs in. Screens that listen
///     to the keyboard of the whole system use it to leave out what is typed into other windows.
/// </summary>
internal static class TerminalFocus
{
    /// <summary>
    ///     The window class of the stand-in console window a terminal application gives its programs.
    /// </summary>
    internal const string PseudoConsoleClass = "PseudoConsoleWindow";

    private const uint RootOwner = 3;
    private const int ClassNameChars = 256;

    /// <summary>
    ///     Whether the terminal is the window in front, or null when that cannot be found out.
    ///     What to do then is up to the caller: the hotkey screen still hears the key, while a dictated
    ///     phrase is still typed into the window in front.
    /// </summary>
    public static bool? IsFocused()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        return IsFocusedOnWindows();
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
        if (!OperatingSystem.IsWindows())
            return true;

        return IsHostedByTerminalOnWindows();
    }

    [SupportedOSPlatform("windows")]
    private static bool IsHostedByTerminalOnWindows()
    {
        IntPtr console = GetConsoleWindow();

        if (console == IntPtr.Zero)
            return false;

        char[] name = new char[ClassNameChars];
        int length = GetClassName(console, name, name.Length);

        return new string(name, 0, Math.Max(0, length)) == PseudoConsoleClass;
    }

    [SupportedOSPlatform("windows")]
    private static bool? IsFocusedOnWindows()
    {
        IntPtr console = GetConsoleWindow();

        if (console == IntPtr.Zero)
            return null;

        char[] name = new char[ClassNameChars];
        int length = GetClassName(console, name, name.Length);

        return Decide(
            console,
            GetAncestor(console, RootOwner),
            GetForegroundWindow(),
            IsWindowVisible(console),
            new string(name, 0, Math.Max(0, length)));
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