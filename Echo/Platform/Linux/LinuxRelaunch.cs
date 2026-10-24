using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Echo.Platform.Linux;

/// <inheritdoc cref="IRelaunch"/>
/// <remarks>
///     The process replaces itself with the new start (execv): the same process in the same terminal, with nothing
///     left behind. The system gives a program its real path even when it was started through a link, so the version
///     folder never needs to be resolved here.
///
///     .NET changes the settings of the terminal while it reads keys and gives them back only when the process exits,
///     which a replaced process never does. So the settings the terminal had when Echo started are kept, and given back
///     just before the replacement: otherwise the next start would take the changed ones for the terminal's own, and
///     leave the shell without the echo of typed letters.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class LinuxRelaunch : IRelaunch
{
    private const string LibC = "libc";
    private const int StandardInput = 0;

    /// <summary>
    ///     TCSANOW: the settings change at once.
    /// </summary>
    private const int ChangeNow = 0;

    /// <summary>
    ///     SIG_SETMASK: the blocked signals become exactly the ones given.
    /// </summary>
    private const int SetMask = 2;

    /// <summary>
    ///     Room for the 'termios' structure of the terminal settings (60 bytes) and for the signal set (128 bytes),
    ///     which Echo only keeps and gives back without reading them.
    /// </summary>
    private const int TerminalSettingsSize = 64;
    private const int SignalSetSize = 128;

    /// <summary>
    ///     The settings of the terminal as they were when Echo started; null without a terminal.
    /// </summary>
    private readonly byte[]? _terminal = TerminalSettings();

    /// <inheritdoc/>
    public int? RunFromVersionFolder(IReadOnlyList<string> arguments) => null;

    /// <inheritdoc/>
    public int Restart(string executable, IReadOnlyList<string> arguments)
    {
        if (_terminal is not null)
            SetTerminalSettings(StandardInput, ChangeNow, _terminal);

        // The signals blocked in this thread would stay blocked in the new start, Ctrl+C among them
        SetSignalMask(SetMask, new byte[SignalSetSize], IntPtr.Zero);

        Execute(executable, [executable, .. arguments, null]);

        // Only a replacement that failed comes back here
        Console.Error.WriteLine(Strings.Format(nameof(Strings.RestartFailed), Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError())));

        return 1;
    }

    private static byte[]? TerminalSettings()
    {
        byte[] settings = new byte[TerminalSettingsSize];

        return IsTerminal(StandardInput) == 1 && GetTerminalSettings(StandardInput, settings) == 0 ? settings : null;
    }

    [DllImport(LibC, EntryPoint = "isatty")]
    private static extern int IsTerminal(int descriptor);

    [DllImport(LibC, EntryPoint = "tcgetattr")]
    private static extern int GetTerminalSettings(int descriptor, byte[] settings);

    [DllImport(LibC, EntryPoint = "tcsetattr")]
    private static extern int SetTerminalSettings(int descriptor, int when, byte[] settings);

    [DllImport(LibC, EntryPoint = "sigprocmask")]
    private static extern int SetSignalMask(int how, byte[] signals, IntPtr previous);

    [DllImport(LibC, EntryPoint = "execv", SetLastError = true)]
    private static extern int Execute(string path, string?[] arguments);
}