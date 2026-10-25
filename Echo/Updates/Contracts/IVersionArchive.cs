using Echo.Setup.Downloads;

namespace Echo.Updates.Contracts;

/// <summary>
///     Unpacks the archive a version of Echo comes in, a zip archive. Its table of contents, at the end, names every
///     file with its size and checksum, and each file can be read from it without the others: so a new version can be
///     made from the files of the version in use, with only the files that changed downloaded.
/// </summary>
internal interface IVersionArchive
{
    /// <summary>
    ///     Unpacks a downloaded archive into <paramref name="directory"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The archive is damaged.</exception>
    void Unpack(string archive, string directory);

    /// <summary>
    ///     Fills <paramref name="directory"/> with the files of a release without downloading its archive whole:
    ///     the files that <paramref name="sameFrom"/> has with the same contents are copied from there, and only the
    ///     others are read from the archive on the server.
    /// </summary>
    /// <param name="release">The release, one that can be installed.</param>
    /// <param name="sameFrom">The folder of the version in use.</param>
    /// <param name="directory">The folder of the new version.</param>
    /// <param name="progress">How much of what has to be downloaded is.</param>
    /// <exception cref="HttpRequestException">The server could not be reached, or does not give parts of a file.</exception>
    /// <exception cref="IOException">The connection was lost, or a file could not be written.</exception>
    /// <exception cref="TimeoutException">The server stopped sending.</exception>
    /// <exception cref="InvalidDataException">The archive is damaged.</exception>
    void UnpackChanges(ReleaseInfo release, string sameFrom, string directory, IProgress<DownloadProgress> progress);
}