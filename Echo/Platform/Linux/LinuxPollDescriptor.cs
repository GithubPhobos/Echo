namespace Echo.Platform.Linux;

/// <summary>One descriptor passed to poll to check the terminal without waiting.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct LinuxPollDescriptor
{
    public int File;
    public short Events;
    public short Returned;
}