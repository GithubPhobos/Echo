namespace Echo.Setup.Downloads.Contracts;

/// <summary>
///     Downloads large files so that an interrupted download continues where it stopped.
/// </summary>
internal interface IFileDownloader
{
    /// <summary>
    ///     Downloads the file to <paramref name="destinationPath"/> and verifies its checksum.
    ///     A partial file is kept on cancellation and on network errors, and reused by the next call.
    /// </summary>
    /// <exception cref="DownloadException">The server refused or stopped sending, or the checksum did not match.</exception>
    Task DownloadAsync(DownloadRequest request,
                       string destinationPath,
                       IProgress<DownloadProgress> progress,
                       CancellationToken cancellationToken);

    /// <summary>
    ///     How many bytes <see cref="DownloadAsync"/> still has to write: what an earlier attempt left
    ///     in a partial file is not downloaded again.
    /// </summary>
    long RemainingBytes(DownloadRequest request, string destinationPath);
}