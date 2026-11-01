namespace Echo.Platform.Windows;

/// <summary>Reads console keys and focus records together, before Console.KeyAvailable can discard the latter.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTerminalInput : ITerminalInput
{
    private readonly IntPtr _handle;
    private readonly TerminalFocusReports _reports;
    private readonly Func<ConsoleInputRecord?> _read;
    private ConsoleKeyInfo _repeated;
    private int _remaining;

    /// <summary>Uses the real dispatcher with a controlled record source in tests.</summary>
    internal WindowsTerminalInput(TerminalFocusReports reports, Func<ConsoleInputRecord?> read)
    {
        _reports = reports;
        _read = read;
    }

    private WindowsTerminalInput(TerminalFocusReports reports, IntPtr handle)
    {
        _reports = reports;
        _handle = handle;
        _read = ReadRecord;
    }

    /// <summary>A native console, including ConPTY, or null for redirected input.</summary>
    internal static WindowsTerminalInput? TryOpen(TerminalFocusReports reports)
    {
        IntPtr handle = GetStdHandle(-10);
        return GetConsoleMode(handle, out _) ? new WindowsTerminalInput(reports, handle) : null;
    }

    /// <inheritdoc/>
    public ConsoleKeyInfo? ReadKey()
    {
        if (_remaining > 0)
        {
            _remaining--;
            return _repeated;
        }
        while (_read() is { } record)
        {
            if (record.Type == 0x10)
            {
                _reports.Report(record.IsDownOrFocused != 0);
                continue;
            }
            if (record.Type != 1)
                continue;
            // Alt+numeric/IME text may arrive on release, without a matching key-down character.
            if (record.IsDownOrFocused == 0 && !(record.Key == 0x12 && record.Character != '\0'))
                continue;
            if (record.Character == '\0' && record.Key is 0x10 or 0x11 or 0x12 or 0x14 or 0x90 or 0x91)
                continue;
            _repeated = new ConsoleKeyInfo(record.Character, (ConsoleKey)record.Key,
                shift: (record.Modifiers & 0x10) != 0,
                alt: (record.Modifiers & 0x03) != 0,
                control: (record.Modifiers & 0x0c) != 0);
            _remaining = Math.Max(1, (int)record.Repeat) - 1;
            return _repeated;
        }
        return null;
    }

    private ConsoleInputRecord? ReadRecord()
    {
        if (!PeekConsoleInputW(_handle, out _, 1, out uint available))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (available == 0)
            return null;
        if (!ReadConsoleInputW(_handle, out ConsoleInputRecord record, 1, out uint read))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return read == 0 ? null : record;
    }

    /// <inheritdoc/>
    public void Dispose() { }

    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int standard);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PeekConsoleInputW(IntPtr handle, out ConsoleInputRecord record, uint count, out uint read);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadConsoleInputW(IntPtr handle, out ConsoleInputRecord record, uint count, out uint read);
}