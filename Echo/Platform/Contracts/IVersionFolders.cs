using System.IO.Compression;

namespace Echo.Platform.Contracts;

/// <summary>
///     How this system keeps the installed versions of Echo: the archive a version comes in, the link to the version
///     in use, and when the folder of a version may be removed.
/// </summary>
internal interface IVersionFolders
{
    /// <summary>
    ///     The name of the zip archive of this system in every release, such as "echooo-linux-x64.zip".
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
    ///     Gives a file unpacked from the archive of a version what the archive keeps about it besides its contents,
    ///     such as the permission to run it.
    /// </summary>
    void ApplyAttributes(ZipArchiveEntry entry, string file);

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