namespace Echo.Platform.Linux;

/// <inheritdoc cref="IVersionFolders"/>
/// <remarks>
///     The link is a symbolic link. A program started through it runs from the folder the link led to at that moment:
///     the system gives the program its real path.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class LinuxVersionFolders : IVersionFolders
{
    private const string LibC = "libc";

    /// <summary>
    ///     Where the system shows every process, each in a folder named by its number.
    /// </summary>
    private const string Processes = "/proc";

    /// <summary>
    ///     The permissions of a file: to read, write and run it, for its owner, its group and everyone else.
    /// </summary>
    private const int PermissionBits = 0x1FF;

    /// <inheritdoc/>
    /// <remarks>
    ///     The install script takes the tar.gz archive of the release instead, since every system has tar and not every
    ///     one has unzip. Echo takes the zip archive, whose files can be read one by one, for updates.
    /// </remarks>
    public string ArchiveName => "echooo-linux-x64.zip";

    /// <inheritdoc/>
    public string ExecutableName => ApplicationInfo.CommandName;

    /// <inheritdoc/>
    public string InstallCommand => "curl -fsSL https://githubphobos.github.io/Echo/install.sh | sh";

    /// <inheritdoc/>
    /// <remarks>
    ///     The zip program of Linux keeps the permissions of a file in the upper half of its attributes. An archive made
    ///     on Windows has nothing there, and its files keep the permissions they were created with.
    /// </remarks>
    public void ApplyAttributes(ZipArchiveEntry entry, string file)
    {
        var mode = (UnixFileMode)((entry.ExternalAttributes >> 16) & PermissionBits);

        if (mode != UnixFileMode.None)
            File.SetUnixFileMode(file, mode);
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The new link is made next to the old one and renamed over it, which is a single step: the link is never
    ///     missing, and never leads into a folder still being filled. It leads to "versions/2.0.1" relative to itself,
    ///     as the install script makes it.
    /// </remarks>
    public void PointTo(string link, string directory)
    {
        string fresh = link + ".new";

        File.Delete(fresh);
        File.CreateSymbolicLink(fresh, Path.GetRelativePath(Path.GetDirectoryName(link)!, directory));

        if (Rename(fresh, link) != 0)
        {
            string reason = Marshal.GetPInvokeErrorMessage(Marshal.GetLastPInvokeError());

            File.Delete(fresh);

            throw new IOException($"{link} cannot be pointed at {directory}: {reason}");
        }
    }

    /// <inheritdoc/>
    public bool TryRemove(string directory)
    {
        // Linux lets the files of a running program be deleted, and the program fails only later, when it loads
        // one of them: so the folder is left alone while any process runs a program from it
        if (IsRunning(directory))
            return false;

        try
        {
            Directory.Delete(directory, recursive: true);

            return true;
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Whether a process runs a program from <paramref name="directory"/>: "/proc/&lt;number&gt;/exe" is a link
    ///     to the program file of every process. Those of other users cannot be read, and do not matter.
    /// </summary>
    internal static bool IsRunning(string directory)
    {
        foreach (string process in Directory.EnumerateDirectories(Processes))
        {
            if (!int.TryParse(Path.GetFileName(process), out _))
                continue;

            try
            {
                if (File.ResolveLinkTarget(Path.Combine(process, "exe"), returnFinalTarget: false)?.FullName is { } program
                    && PathNames.IsInside(program, directory))
                {
                    return true;
                }
            }
            catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
            {
                // The process has ended meanwhile, or belongs to someone else
            }
        }

        return false;
    }

    [DllImport(LibC, EntryPoint = "rename", SetLastError = true)]
    private static extern int Rename(string source, string destination);
}