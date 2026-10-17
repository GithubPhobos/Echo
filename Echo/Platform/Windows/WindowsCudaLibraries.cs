using System.IO.Compression;
using Echo.Setup.Downloads;
using Echo.Setup.Hardware;

namespace Echo.Platform.Windows;

/// <summary>
///     The CUDA libraries of Windows: DLL files in ZIP archives, found next to the executable or on the search path.
/// </summary>
internal sealed class WindowsCudaLibraries : ICudaLibraries
{
    /// <summary>
    ///     CUDA 13.4.2, the version the bundled Whisper runtime is built against. Newer NVIDIA releases
    ///     do not matter: this table changes only together with the Whisper runtime.
    /// </summary>
    private static readonly IReadOnlyList<CudaArchive> _archives =
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

    /// <inheritdoc/>
    public IReadOnlyList<CudaArchive> Archives => _archives;

    /// <inheritdoc/>
    public double UnpackedSizeFactor => 1.5;

    /// <summary>
    ///     Besides <paramref name="directory"/>: next to the executable (a manual install) and wherever
    ///     a CUDA toolkit put them on the search path.
    /// </summary>
    /// <inheritdoc/>
    public IReadOnlyList<string> FindMissing(string directory)
    {
        IEnumerable<string> searchPath = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        string[] directories = [directory, AppContext.BaseDirectory, .. searchPath];

        return Archives
            .SelectMany(archive => archive.Libraries)
            .Where(library => !directories.Any(candidate => Exists(candidate, library)))
            .ToList();
    }

    /// <summary>
    ///     ZIP archives are unpacked by .NET itself.
    /// </summary>
    /// <inheritdoc/>
    public IReadOnlyList<string> MissingTools() => [];

    /// <summary>
    ///     Takes the listed library files out of an archive, wherever inside it they are.
    /// </summary>
    /// <inheritdoc/>
    public void Extract(string archivePath, CudaArchive archive, string stagingDirectory)
    {
        using ZipArchive zip = ZipFile.OpenRead(archivePath);

        foreach (string library in archive.Libraries)
        {
            ZipArchiveEntry entry = zip.Entries.FirstOrDefault(
                candidate => string.Equals(candidate.Name, library, StringComparison.OrdinalIgnoreCase))
                ?? throw new DownloadException($"'{library}' is missing from {Path.GetFileName(archivePath)}.");

            entry.ExtractToFile(Path.Combine(stagingDirectory, library), overwrite: true);
        }
    }

    /// <inheritdoc/>
    public void MoveIntoPlace(string stagingDirectory, string library, string directory) =>
        File.Move(Path.Combine(stagingDirectory, library), Path.Combine(directory, library), overwrite: true);

    /// <summary>
    ///     Windows looks for the libraries a library needs on the search path, so the folder is put first on it.
    /// </summary>
    /// <inheritdoc/>
    public void PrepareForLoading(string directory)
    {
        if (!Directory.Exists(directory))
            return;

        string searchPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        if (!searchPath.Split(Path.PathSeparator).Contains(directory, StringComparer.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + searchPath);
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