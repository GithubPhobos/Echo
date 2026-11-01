namespace Echo.Platform.Linux;

/// <summary>The Linux x64 termios layout, the platform supported by Echo's Linux build.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LinuxTerminalMode
{
    public uint Input;
    public uint Output;
    public uint Control;
    public uint Local;
    public byte Line;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Characters;
    public uint InputSpeed;
    public uint OutputSpeed;
}