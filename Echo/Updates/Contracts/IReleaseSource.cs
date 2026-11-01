namespace Echo.Updates.Contracts;

/// <summary>
///     Where the published versions of Echo are found.
/// </summary>
internal interface IReleaseSource
{
    /// <summary>
    ///     The latest published version, with the archive for this system.
    /// </summary>
    /// <exception cref="HttpRequestException">The releases could not be reached or refused.</exception>
    /// <exception cref="InvalidDataException">The answer is not a release Echo can install.</exception>
    Task<ReleaseInfo> LatestAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     The SHA-256 checksum the release gives for its archive, in hexadecimal.
    /// </summary>
    /// <exception cref="HttpRequestException">The list of checksums could not be downloaded.</exception>
    /// <exception cref="InvalidDataException">The list does not name the archive.</exception>
    Task<string> ChecksumAsync(ReleaseInfo release, CancellationToken cancellationToken);
}