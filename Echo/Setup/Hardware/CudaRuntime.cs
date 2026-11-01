namespace Echo.Setup.Hardware;

/// <inheritdoc cref="ICudaRuntime"/>
/// <param name="paths"><see cref="IAppPaths"/></param>
/// <param name="downloader"><see cref="IFileDownloader"/></param>
/// <param name="libraries">What the libraries are on this system, and how they are unpacked and found.</param>
internal sealed class CudaRuntime(IAppPaths paths, IFileDownloader downloader, ICudaLibraries libraries) : ICudaRuntime
{
    public const string DownloadRoot = "https://developer.download.nvidia.com/compute/cuda/redist/";

    private const string StagingFolder = ".download";

    private readonly IAppPaths _paths = paths;
    private readonly IFileDownloader _downloader = downloader;
    private readonly ICudaLibraries _libraries = libraries;

    /// <inheritdoc/>
    public string Directory => _paths.CudaDirectory;

    /// <inheritdoc/>
    public long DownloadSizeBytes => _libraries.Archives.Sum(archive => archive.SizeBytes);

    /// <inheritdoc/>
    public long RequiredSpaceBytes()
    {
        IReadOnlyList<string> missing = FindMissingLibraries();

        // An archive and what is unpacked from it are on the disk at the same time
        return _libraries.Archives
            .Where(archive => archive.Libraries.Any(missing.Contains))
            .Sum(archive => _downloader.RemainingBytes(Request(archive), ArchivePath(archive))
                + (long)(archive.SizeBytes * _libraries.UnpackedSizeFactor));
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Libraries => [.. _libraries.Archives.SelectMany(archive => archive.Libraries)];

    /// <inheritdoc/>
    public IReadOnlyList<string> FindMissingLibraries() =>
        _libraries.FindMissing(Directory);

    /// <inheritdoc/>
    public IReadOnlyList<string> MissingTools() =>
        _libraries.MissingTools();

    /// <inheritdoc/>
    public async Task InstallAsync(IProgress<DownloadProgress> progress, IProgress<string> downloaded, CancellationToken cancellationToken)
    {
        // Archives are downloaded and unpacked aside, so nothing unfinished is ever taken for a library.
        // A stopped download leaves only some of the libraries in place; that is safe, because the Whisper
        // runtime is pointed at CUDA only once FindMissingLibraries finds nothing missing
        string staging = StagingDirectory;
        System.IO.Directory.CreateDirectory(staging);

        IReadOnlyList<string> missing = FindMissingLibraries();

        long total = DownloadSizeBytes;
        long finished = 0;

        foreach (CudaArchive archive in _libraries.Archives)
        {
            long before = finished;

            finished += archive.SizeBytes;

            // Brought by an earlier attempt, or put there by the user
            if (!archive.Libraries.Any(missing.Contains))
            {
                progress.Report(new DownloadProgress(finished, total, 0, IsVerifying: false));

                continue;
            }

            string archivePath = ArchivePath(archive);

            // Progress is reported across all archives as if they were one file
            var archiveProgress = new InlineProgress<DownloadProgress>(update => progress.Report(
                update with { BytesDone = before + update.BytesDone, BytesTotal = total }));

            await _downloader.DownloadAsync(Request(archive), archivePath, archiveProgress, cancellationToken);

            _libraries.Extract(archivePath, archive, staging);
            File.Delete(archivePath);

            foreach (string library in archive.Libraries)
            {
                _libraries.MoveIntoPlace(staging, library, Directory);

                downloaded.Report(library);
            }
        }

        System.IO.Directory.Delete(staging, recursive: true);
    }

    /// <summary>
    ///     Where archives are downloaded and unpacked before the libraries are put in place.
    /// </summary>
    private string StagingDirectory => Path.Combine(Directory, StagingFolder);

    private string ArchivePath(CudaArchive archive) =>
        Path.Combine(StagingDirectory, Path.GetFileName(archive.RelativePath));

    private static DownloadRequest Request(CudaArchive archive) =>
        new(new Uri(DownloadRoot + archive.RelativePath), archive.SizeBytes, archive.Sha256);
}