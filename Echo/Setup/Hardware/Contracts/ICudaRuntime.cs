namespace Echo.Setup.Hardware.Contracts;

/// <summary>
///     The NVIDIA libraries the CUDA backend needs at run time.
/// </summary>
internal interface ICudaRuntime
{
    /// <summary>
    ///     Where downloaded or manually copied libraries belong.
    /// </summary>
    string Directory { get; }

    /// <summary>
    ///     The size of everything <see cref="InstallAsync"/> downloads.
    /// </summary>
    long DownloadSizeBytes { get; }

    /// <summary>
    ///     The free space <see cref="InstallAsync"/> still needs: what is left to download of the archives
    ///     of the missing libraries, and the libraries unpacked next to them.
    /// </summary>
    long RequiredSpaceBytes();

    /// <summary>
    ///     The file names of all the libraries the backend needs.
    /// </summary>
    IReadOnlyList<string> Libraries { get; }

    /// <summary>
    ///     The names of the required libraries that cannot be found anywhere.
    /// </summary>
    IReadOnlyList<string> FindMissingLibraries();

    /// <summary>
    ///     The programs of the system that unpacking needs and that are not installed. Nothing is downloaded
    ///     while this list is not empty.
    /// </summary>
    IReadOnlyList<string> MissingTools();

    /// <summary>
    ///     Downloads the libraries that are still missing from NVIDIA and unpacks them into <see cref="Directory"/>.
    ///     Every library is put in place as soon as it has arrived, so a download that was stopped does not
    ///     start over with what it already brought.
    /// </summary>
    /// <param name="progress">Told how much of the whole download is done.</param>
    /// <param name="downloaded">Told the name of every library as soon as it is downloaded.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    Task InstallAsync(IProgress<DownloadProgress> progress, IProgress<string> downloaded, CancellationToken cancellationToken);
}