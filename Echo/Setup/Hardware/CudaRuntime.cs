using System.IO.Compression;
using System.Runtime.InteropServices;
using Echo.Setup.Downloads;

namespace Echo.Setup.Hardware;

/// <inheritdoc cref="ICudaRuntime"/>
/// <param name="paths"><see cref="IAppPaths"/></param>
/// <param name="downloader"><see cref="IFileDownloader"/></param>
internal sealed class CudaRuntime(IAppPaths paths, IFileDownloader downloader) : ICudaRuntime
{
    public const string DownloadRoot = "https://developer.download.nvidia.com/compute/cuda/redist/";

    /// <summary>
    ///     CUDA 13.4.2, the version the bundled Whisper runtime is built against. Newer NVIDIA releases
    ///     do not matter: this table changes only together with the Whisper runtime.
    /// </summary>
    public static readonly IReadOnlyList<CudaArchive> Archives =
    [
        new("cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-13.4.92-archive.zip",
            2_737_397,
            "621a70c4778287b1abf8c27c61b841d797fda2aeff01033374dd19624bcfa249",
            ["cudart64_13.dll"]),

        new("libcublas/windows-x86_64/libcublas-windows-x86_64-13.8.0.4-archive.zip",
            422_415_931,
            "0974318e9861a61cb9091cf7de8d4c2880e9787fe311cb4bcbd08865663c5f43",
            ["cublas64_13.dll", "cublasLt64_13.dll"]),
    ];

    private const string StagingFolder = ".download";

    /// <summary>
    ///     How much room the libraries of an archive take once unpacked, for every byte of the archive.
    /// </summary>
    internal const double UnpackedSizeFactor = 1.5;

    private readonly IAppPaths _paths = paths;
    private readonly IFileDownloader _downloader = downloader;

    /// <inheritdoc/>
    public string Directory => _paths.CudaDirectory;

    /// <inheritdoc/>
    public long DownloadSizeBytes => Archives.Sum(archive => archive.SizeBytes);

    /// <inheritdoc/>
    public long RequiredSpaceBytes()
    {
        IReadOnlyList<string> missing = FindMissingLibraries();

        // An archive and what is unpacked from it are on the disk at the same time
        return Archives
            .Where(archive => archive.Libraries.Any(missing.Contains))
            .Sum(archive => _downloader.RemainingBytes(Request(archive), ArchivePath(archive))
                + (long)(archive.SizeBytes * UnpackedSizeFactor));
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Libraries { get; } = [.. Archives.SelectMany(archive => archive.Libraries)];

    /// <inheritdoc/>
    public IReadOnlyList<string> FindMissingLibraries() =>
        FindMissingLibraries(Directory);

    /// <summary>
    ///     The names of the required libraries that cannot be found in <paramref name="directory"/>,
    ///     next to the executable, or anywhere on the search path.
    /// </summary>
    /// <remarks>
    ///     The Whisper runtime must never be pointed at CUDA while this list is not empty: with only some
    ///     of the libraries in place it aborts the whole process instead of falling back.
    /// </remarks>
    public static IReadOnlyList<string> FindMissingLibraries(string directory)
    {
        // Besides our own folder: next to the executable (a manual install) and wherever a CUDA toolkit put them
        IEnumerable<string> searchPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        string[] directories = [directory, AppContext.BaseDirectory, .. searchPath];

        return Archives
            .SelectMany(archive => archive.Libraries)
            .Where(library => !directories.Any(candidate => Exists(candidate, library)))
            .ToList();
    }

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

        foreach (CudaArchive archive in Archives)
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

            Extract(archivePath, archive.Libraries, staging);
            File.Delete(archivePath);

            foreach (string library in archive.Libraries)
            {
                File.Move(Path.Combine(staging, library), Path.Combine(Directory, library), overwrite: true);

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

    /// <summary>
    ///     Makes the libraries in <paramref name="directory"/> visible to the Whisper runtime.
    ///     Must be called before the runtime loads its native library.
    /// </summary>
    public static void RegisterSearchPath(string directory)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !System.IO.Directory.Exists(directory))
            return;

        string searchPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        if (!searchPath.Split(Path.PathSeparator).Contains(directory, StringComparer.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + searchPath);
    }

    /// <summary>
    ///     Takes the listed library files out of an archive, wherever inside it they are.
    /// </summary>
    internal static void Extract(string archivePath, IReadOnlyList<string> libraries, string targetDirectory)
    {
        using ZipArchive zip = ZipFile.OpenRead(archivePath);

        foreach (string library in libraries)
        {
            ZipArchiveEntry entry = zip.Entries.FirstOrDefault(
                candidate => string.Equals(candidate.Name, library, StringComparison.OrdinalIgnoreCase))
                ?? throw new DownloadException($"'{library}' is missing from {Path.GetFileName(archivePath)}.");

            entry.ExtractToFile(Path.Combine(targetDirectory, library), overwrite: true);
        }
    }

    private static bool Exists(string directory, string library)
    {
        try
        {
            return File.Exists(Path.Combine(directory, library));
        }
        catch (ArgumentException)
        {
            // A malformed entry in the search path
            return false;
        }
    }
}