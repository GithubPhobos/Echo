namespace Echo.Updates;

/// <summary>
///     The right to change the installed versions: their folders and the link "current". One Echo or one install
///     script holds it at a time, across processes, so one does not remove a version another is putting in place.
///     It is a file the system locks: Windows lets no one else open a file opened without sharing, and on Linux .NET
///     locks it with flock, as the install script does with the flock program.
/// </summary>
internal sealed class InstallLock : IDisposable
{
    private static readonly TimeSpan _retryInterval = TimeSpan.FromMilliseconds(200);

    private readonly FileStream _file;

    private InstallLock(FileStream file)
    {
        _file = file;
    }

    /// <summary>
    ///     Takes the lock, or returns null while another Echo or the install script holds it.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The folder of the installation cannot be written.</exception>
    public static InstallLock? TryTake(InstallLayout layout)
    {
        try
        {
            return new InstallLock(new FileStream(layout.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException exc) when (exc is not DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Takes the lock, waiting while another Echo or the install script holds it.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The folder of the installation cannot be written.</exception>
    public static async Task<InstallLock> TakeAsync(InstallLayout layout)
    {
        while (true)
        {
            if (TryTake(layout) is { } taken)
                return taken;

            await Task.Delay(_retryInterval);
        }
    }

    public void Dispose()
    {
        _file.Dispose();
    }
}