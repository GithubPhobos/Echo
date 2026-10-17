namespace Echo.Infrastructure;

/// <inheritdoc cref="ISystemShell"/>
/// <param name="fileChooser">The file chooser of the system.</param>
internal sealed class SystemShell(IFileChooser fileChooser) : ISystemShell
{
    private readonly IFileChooser _fileChooser = fileChooser;

    /// <inheritdoc/>
    public bool CanPickFiles => _fileChooser.IsAvailable;

    /// <inheritdoc/>
    public long GetFreeSpace(string path)
    {
        try
        {
            return FreeSpaceOf(path, DriveInfo.GetDrives().Select(drive => drive.RootDirectory.FullName), root => new DriveInfo(root).AvailableFreeSpace);
        }
        catch (Exception exc) when (exc is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Not knowing is no reason to refuse a download; a full disk will fail it with a clear error anyway
            return long.MaxValue;
        }
    }

    /// <summary>
    ///     The free space of the file system the path is on. On Linux a disk is a folder anywhere in the tree,
    ///     such as '/home', so the deepest folder that holds the path is the one that counts, not the root.
    /// </summary>
    /// <param name="path">The folder that will get the files; it need not exist yet.</param>
    /// <param name="roots">Where the file systems start.</param>
    /// <param name="freeSpace">Tells the free space of a file system by where it starts.</param>
    internal static long FreeSpaceOf(string path, IEnumerable<string> roots, Func<string, long> freeSpace)
    {
        string? root = roots
            .Where(candidate => PathNames.IsInside(path, candidate))
            .MaxBy(candidate => Path.GetFullPath(candidate).Length);

        return root is null ? long.MaxValue : freeSpace(root);
    }

    /// <inheritdoc/>
    public void CreateDirectory(string path) =>
        Directory.CreateDirectory(path);

    /// <inheritdoc/>
    public void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            // Windows opens the folder in Explorer; on Linux .NET hands it to the desktop's own opener (xdg-open)
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exc) when (exc is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // The path is on the screen as well, so a missing file manager is not a problem
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> PickFilesAsync(string title, string pattern, CancellationToken cancellationToken) =>
        _fileChooser.PickAsync(title, pattern, cancellationToken);
}