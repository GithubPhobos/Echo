namespace Echo.Platform.Contracts;

/// <summary>
///     How this system keeps the installed versions of Echo: the archive a version comes in, the link to the version
///     in use, and when the folder of a version may be removed.
/// </summary>
internal interface IVersionFolders
{
    /// <summary>
    ///     The name of the archive of this system in every release, such as "echooo-linux-x64.tar.gz".
    /// </summary>
    string ArchiveName { get; }

    /// <summary>
    ///     The name of Echo's program file, such as "echooo.exe".
    /// </summary>
    string ExecutableName { get; }

    /// <summary>
    ///     The line that installs Echo in a terminal of this system.
    /// </summary>
    string InstallCommand { get; }

    /// <summary>
    ///     Unpacks the archive of a version into <paramref name="directory"/>, keeping what makes the program runnable.
    /// </summary>
    void Unpack(string archive, string directory);

    /// <summary>
    ///     Points <paramref name="link"/> at <paramref name="directory"/>, creating the link where there is none:
    ///     whatever starts through the link from then on starts from that folder.
    /// </summary>
    void PointTo(string link, string directory);

    /// <summary>
    ///     Removes the folder of a version unless an Echo runs from it. Returns whether it is gone.
    /// </summary>
    bool TryRemove(string directory);
}