using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Echo.Listening;

namespace Echo.Platform.Linux;

/// <summary>
///     Compares the window of the terminal with the window the desktop says is in front. Many X11 terminals
///     (xterm, Konsole, kitty, Alacritty) name their window in the WINDOWID variable; for the ones that do not,
///     such as GNOME Terminal, what the terminal itself reports is taken.
/// </summary>
/// <param name="variable">Reads an environment variable.</param>
/// <param name="reports">What the terminal itself says about its window.</param>
[SupportedOSPlatform("linux")]
internal sealed class X11TerminalFocus(Func<string, string?> variable, TerminalFocusReports reports) : ITerminalFocus
{
    private const string ActiveWindowAtom = "_NET_ACTIVE_WINDOW";

    /// <summary>
    ///     How far up the tree of windows the terminal's window is followed: a terminal names its inner window,
    ///     the desktop the outer one.
    /// </summary>
    private const int MaxDepth = 16;

    private readonly Func<string, string?> _variable = variable;
    private readonly TerminalFocusReports _reports = reports;

    /// <inheritdoc/>
    public bool? IsFocused() =>
        FromDesktop() ?? _reports.IsFocused;

    /// <summary>
    ///     What the desktop says, or null when the terminal does not name its window.
    /// </summary>
    private bool? FromDesktop()
    {
        if (!ulong.TryParse(_variable("WINDOWID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong terminal) || terminal == 0)
            return null;

        IntPtr display = X11Native.OpenDisplay(null);
        if (display == IntPtr.Zero)
            return null;

        try
        {
            ulong? active = ActiveWindow(display);

            return active is null ? null : Decide(terminal, active.Value, window => Parent(display, window));
        }
        finally
        {
            X11Native.CloseDisplay(display);
        }
    }

    /// <summary>
    ///     Whether the active window is the terminal's window or one that holds it.
    /// </summary>
    /// <param name="terminal">The window the terminal named.</param>
    /// <param name="active">The window in front.</param>
    /// <param name="parent">Gives the window that holds a window, or null at the top.</param>
    internal static bool Decide(ulong terminal, ulong active, Func<ulong, ulong?> parent)
    {
        ulong? window = terminal;

        for (int depth = 0; window is not null && depth < MaxDepth; depth++)
        {
            if (window == active)
                return true;

            window = parent(window.Value);
        }

        return false;
    }

    private static ulong? ActiveWindow(IntPtr display)
    {
        ulong atom = X11Native.InternAtom(display, ActiveWindowAtom, 1);
        if (atom == 0)
            return null;

        int status = X11Native.GetWindowProperty(
            display, X11Native.DefaultRootWindow(display), atom, 0, 1, 0, X11Native.WindowType,
            out _, out int format, out ulong count, out _, out IntPtr value);

        try
        {
            // A window id comes as one "long" of the machine
            return status == 0 && format == 32 && count == 1 && value != IntPtr.Zero ? (ulong)Marshal.ReadInt64(value) : null;
        }
        finally
        {
            if (value != IntPtr.Zero)
                X11Native.Free(value);
        }
    }

    private static ulong? Parent(IntPtr display, ulong window)
    {
        if (X11Native.QueryTree(display, window, out ulong root, out ulong parent, out IntPtr children, out _) == 0)
            return null;

        if (children != IntPtr.Zero)
            X11Native.Free(children);

        return parent == 0 || parent == root ? null : parent;
    }
}