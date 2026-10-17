using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Echo.Setup.Downloads;
using Echo.Setup.Hardware;

namespace Echo.Platform.Linux;

/// <summary>
///     The CUDA libraries of Linux. The NVIDIA driver does not bring them: they come with the CUDA Toolkit or the
///     CUDA library packages of the distribution, or Echo downloads them. A library is loaded by its short name
///     ('libcublas.so.13'), which is a link to the file with the full version in its name.
/// </summary>
/// <param name="programs">Finds tar and xz, which unpack the archives.</param>
[SupportedOSPlatform("linux")]
internal sealed class LinuxCudaLibraries(LinuxPrograms programs) : ICudaLibraries
{
    /// <summary>
    ///     CUDA 13.4.2, the version the bundled Whisper runtime is built against. Newer NVIDIA releases
    ///     do not matter: this table changes only together with the Whisper runtime.
    /// </summary>
    private static readonly IReadOnlyList<CudaArchive> _archives =
    [
        new("cuda_cudart/linux-x86_64/cuda_cudart-linux-x86_64-13.4.92-archive.tar.xz",
            1_704_640,
            "0ac5dbc538d04e9983bc493b410cce4b459e1ee9f5f6654b6464ef7b3e14a8b5",
            ["libcudart.so.13"]),

        // cuBLAS needs its "Lt" part, and Whisper loads both
        new("libcublas/linux-x86_64/libcublas-linux-x86_64-13.8.0.4-archive.tar.xz",
            877_123_136,
            "bd6ffcce561672da6abb928214a6694f392741e7d1e2d7dfde0da4bd651706b6",
            ["libcublasLt.so.13", "libcublas.so.13"]),
    ];

    /// <summary>
    ///     Unpacking takes only the libraries out of an archive that also holds headers and static libraries.
    /// </summary>
    private const double SharedLibrariesShare = 1.0;

    private static readonly TimeSpan _unpackTimeout = TimeSpan.FromMinutes(10);

    private readonly LinuxPrograms _programs = programs;

    /// <summary>
    ///     Libraries loaded by Echo itself, kept for the whole process.
    /// </summary>
    private readonly List<IntPtr> _loaded = [];

    /// <inheritdoc/>
    public IReadOnlyList<CudaArchive> Archives => _archives;

    /// <inheritdoc/>
    public double UnpackedSizeFactor => SharedLibrariesShare;

    /// <summary>
    ///     Besides <paramref name="directory"/>: next to the executable, and wherever the system's own loader finds
    ///     libraries (the folders of the distribution, those the CUDA Toolkit registers, LD_LIBRARY_PATH).
    /// </summary>
    /// <inheritdoc/>
    public IReadOnlyList<string> FindMissing(string directory) =>
        Archives
            .SelectMany(archive => archive.Libraries)
            .Where(library => !File.Exists(Path.Combine(directory, library))
                              && !File.Exists(Path.Combine(AppContext.BaseDirectory, library))
                              && !IsInSystem(library))
            .ToList();

    /// <summary>
    ///     The archives are 'tar.xz'; .NET does not read xz, so the system's tar unpacks them, and tar hands xz
    ///     to the program of the same name.
    /// </summary>
    /// <inheritdoc/>
    public IReadOnlyList<string> MissingTools() =>
        new[] { "tar", "xz" }.Where(tool => !_programs.Exists(tool)).ToList();

    /// <summary>
    ///     Unpacks only the libraries, with their links, keeping the archive's own folders.
    /// </summary>
    /// <inheritdoc/>
    public void Extract(string archivePath, CudaArchive archive, string stagingDirectory)
    {
        var start = new ProcessStartInfo("tar")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (string argument in new[] { "-xJf", archivePath, "-C", stagingDirectory, "--wildcards" })
            start.ArgumentList.Add(argument);

        foreach (string library in archive.Libraries)
            start.ArgumentList.Add($"*/lib/{library}*");

        using Process process = Process.Start(start) ?? throw new DownloadException("Couldn't start tar.");

        string errors = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();

        if (!process.WaitForExit(_unpackTimeout))
        {
            process.Kill(entireProcessTree: true);

            throw new DownloadException($"Unpacking {Path.GetFileName(archivePath)} took too long.");
        }

        if (process.ExitCode != 0)
            throw new DownloadException($"Couldn't unpack {Path.GetFileName(archivePath)}: {errors.Trim()}");

        foreach (string library in archive.Libraries)
        {
            if (Find(stagingDirectory, library) is null)
                throw new DownloadException($"'{library}' is missing from {Path.GetFileName(archivePath)}.");
        }
    }

    /// <summary>
    ///     Moves the file with the full version in its name and makes the short name a link to it, as the
    ///     archive and the distributions have it.
    /// </summary>
    /// <inheritdoc/>
    public void MoveIntoPlace(string stagingDirectory, string library, string directory)
    {
        string unpacked = Find(stagingDirectory, library)
            ?? throw new DownloadException($"'{library}' was not unpacked.");

        FileSystemInfo? target = new FileInfo(unpacked).ResolveLinkTarget(returnFinalTarget: true);
        string file = target?.FullName ?? unpacked;
        string fileName = target is null ? library : Path.GetFileName(file);

        File.Move(file, Path.Combine(directory, fileName), overwrite: true);

        if (fileName == library)
            return;

        string link = Path.Combine(directory, library);

        if (File.Exists(link) || new FileInfo(link).LinkTarget is not null)
            File.Delete(link);

        File.CreateSymbolicLink(link, fileName);
    }

    /// <summary>
    ///     Linux finds the libraries a library needs by their short names, and takes one that is loaded already.
    ///     So the libraries in the folder are loaded first, by their full paths, in the order they need each other.
    /// </summary>
    /// <inheritdoc/>
    public void PrepareForLoading(string directory)
    {
        string[] order = ["libcudart.so.13", "libcublasLt.so.13", "libcublas.so.13"];

        // With any of them missing here, the system's own copies are used, or none
        if (!order.All(library => File.Exists(Path.Combine(directory, library))))
            return;

        lock (_loaded)
        {
            if (_loaded.Count > 0)
                return;

            foreach (string library in order)
            {
                if (NativeLibrary.TryLoad(Path.Combine(directory, library), out IntPtr handle))
                    _loaded.Add(handle);
            }
        }
    }

    /// <summary>
    ///     Whether the system's loader finds the library by its short name.
    /// </summary>
    private static bool IsInSystem(string library)
    {
        if (!NativeLibrary.TryLoad(library, out IntPtr handle))
            return false;

        NativeLibrary.Free(handle);

        return true;
    }

    /// <summary>
    ///     Where in the unpacked archive a library is, by its short name.
    /// </summary>
    private static string? Find(string stagingDirectory, string library) =>
        Directory.EnumerateFileSystemEntries(stagingDirectory, library, SearchOption.AllDirectories).FirstOrDefault();
}