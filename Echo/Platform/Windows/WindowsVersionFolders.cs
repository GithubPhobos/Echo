using Microsoft.Win32.SafeHandles;

namespace Echo.Platform.Windows;

/// <inheritdoc cref="IVersionFolders"/>
/// <remarks>
///     The link is a junction: unlike a symbolic link, any user can make one, without administrator rights or the
///     developer mode. A program started through a junction sees the junction's path as its own, not the version
///     folder's, so the installed Echo is run from the version folder itself (<see cref="WindowsRelaunch"/>).
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsVersionFolders : IVersionFolders
{
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint FlagBackupSemantics = 0x02000000;
    private const uint FlagOpenReparsePoint = 0x00200000;

    /// <summary>
    ///     FSCTL_SET_REPARSE_POINT: turns an empty folder into a link.
    /// </summary>
    private const uint SetReparsePoint = 0x000900A4;

    /// <summary>
    ///     IO_REPARSE_TAG_MOUNT_POINT: the kind of link a junction is.
    /// </summary>
    private const uint MountPointTag = 0xA0000003;

    /// <inheritdoc/>
    public string ArchiveName => "echooo-win-x64.zip";

    /// <inheritdoc/>
    public string ExecutableName => $"{ApplicationInfo.CommandName}.exe";

    /// <inheritdoc/>
    public string InstallCommand => "irm https://githubphobos.github.io/Echo/install.ps1 | iex";

    /// <inheritdoc/>
    /// <remarks>Windows runs a program by its name, not by a permission, so a file needs nothing besides its contents.</remarks>
    public void ApplyAttributes(ZipArchiveEntry entry, string file)
    {
    }

    /// <inheritdoc/>
    public void PointTo(string link, string directory)
    {
        var existing = new DirectoryInfo(link);

        if (existing.LinkTarget is not null)
            existing.Delete();
        else if (existing.Exists)
            throw new IOException($"{link} is a folder, not a link, so it is left as it is.");

        Directory.CreateDirectory(link);

        try
        {
            MakeJunction(link, Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)));
        }
        catch
        {
            Directory.Delete(link);

            throw;
        }
    }

    /// <inheritdoc/>
    public bool TryRemove(string directory)
    {
        try
        {
            // The program file of a running Echo cannot be deleted: while one runs from the folder, it stays whole
            string program = Path.Combine(directory, ExecutableName);

            if (File.Exists(program))
                File.Delete(program);

            Directory.Delete(directory, recursive: true);

            return true;
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Turns the empty folder <paramref name="link"/> into a junction to <paramref name="target"/>.
    /// </summary>
    private static void MakeJunction(string link, string target)
    {
        // The name the system follows, "\??\C:\...", and the one it shows, "C:\...", each ending in a null character
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        byte[] shown = Encoding.Unicode.GetBytes(target);

        int dataLength = 4 * sizeof(ushort) + substitute.Length + sizeof(char) + shown.Length + sizeof(char);
        byte[] buffer = new byte[sizeof(uint) + 2 * sizeof(ushort) + dataLength];

        using (var writer = new BinaryWriter(new MemoryStream(buffer)))
        {
            writer.Write(MountPointTag);
            writer.Write((ushort)dataLength);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)substitute.Length);
            writer.Write((ushort)(substitute.Length + sizeof(char)));
            writer.Write((ushort)shown.Length);
            writer.Write(substitute);
            writer.Write((ushort)0);
            writer.Write(shown);
            writer.Write((ushort)0);
        }

        using SafeFileHandle handle = CreateFile(
            link, GenericWrite, 0, IntPtr.Zero, OpenExisting, FlagBackupSemantics | FlagOpenReparsePoint, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new IOException($"{link} cannot be opened: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");

        if (!DeviceIoControl(handle, SetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new IOException($"{link} cannot be made a link: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name,
                                                    uint access,
                                                    uint share,
                                                    IntPtr security,
                                                    uint creation,
                                                    uint flags,
                                                    IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device,
                                               uint code,
                                               byte[] input,
                                               int inputSize,
                                               IntPtr output,
                                               int outputSize,
                                               out int returned,
                                               IntPtr overlapped);
}