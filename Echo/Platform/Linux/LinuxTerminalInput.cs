namespace Echo.Platform.Linux;

/// <summary>Reads the terminal's UTF-8 stream in noncanonical mode, restoring termios on exit.</summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxTerminalInput : ITerminalInput
{
    private readonly LinuxTerminalMode _previous;
    private readonly TerminalInputParser _parser;
    private readonly byte[] _bytes = new byte[256];
    private bool _disposed;

    private LinuxTerminalInput(LinuxTerminalMode previous, TerminalFocusReports reports)
    {
        _previous = previous;
        _parser = new TerminalInputParser(reports, TimeProvider.System);
    }

    /// <summary>Takes a Linux terminal, leaving redirected input to the existing console reader.</summary>
    internal static LinuxTerminalInput? TryOpen(TerminalFocusReports reports)
    {
        if (tcgetattr(0, out LinuxTerminalMode previous) != 0)
            return null;
        LinuxTerminalMode mode = previous;
        mode.Characters = (byte[])previous.Characters.Clone();
        mode.Input &= ~(1u | 2u | 8u | 32u | 64u | 128u | 256u | 1024u);
        mode.Local &= ~(1u | 2u | 8u | 64u | 32768u);
        mode.Control = (mode.Control & ~(48u | 256u)) | 48u;
        mode.Characters[5] = 0; // VTIME
        mode.Characters[6] = 0; // VMIN: reads never wait for another byte
        var input = new LinuxTerminalInput(previous, reports);
        if (tcsetattr(0, 0, ref mode) != 0)
            return null;
        return input;
    }

    /// <inheritdoc/>
    public ConsoleKeyInfo? ReadKey()
    {
        if (_disposed)
            return null;
        var descriptor = new LinuxPollDescriptor { File = 0, Events = 1 };
        int ready = poll(ref descriptor, 1, 0);
        if (ready > 0 && (descriptor.Returned & 1) != 0)
        {
            nint count = read(0, _bytes, (nuint)_bytes.Length);
            if (count > 0)
                _parser.Feed(_bytes.AsSpan(0, (int)count));
            else if (count < 0 && Marshal.GetLastPInvokeError() is not (4 or 11))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
        else if (ready < 0 && Marshal.GetLastPInvokeError() != 4)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return _parser.ReadKey();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        LinuxTerminalMode previous = _previous;
        if (tcsetattr(0, 0, ref previous) != 0)
            Log.Warning("Could not restore the terminal input mode: {Error}", Marshal.GetLastPInvokeError());
    }

    [DllImport("libc", SetLastError = true)] private static extern int tcgetattr(int fd, out LinuxTerminalMode mode);
    [DllImport("libc", SetLastError = true)] private static extern int tcsetattr(int fd, int action, ref LinuxTerminalMode mode);
    [DllImport("libc", SetLastError = true)] private static extern int poll(ref LinuxPollDescriptor fds, nuint count, int milliseconds);
    [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, byte[] buffer, nuint count);
}