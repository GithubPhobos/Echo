using Echo.Setup.Hardware;

namespace Echo.Platform.Contracts;

/// <summary>
///     The NVIDIA libraries the CUDA build of Whisper needs, as this system names them, ships them
///     and finds them.
/// </summary>
internal interface ICudaLibraries
{
    /// <summary>
    ///     The archives on the NVIDIA server that hold the libraries for this system.
    /// </summary>
    IReadOnlyList<CudaArchive> Archives { get; }

    /// <summary>
    ///     How much room the libraries of an archive take once unpacked, for every byte of the archive.
    /// </summary>
    double UnpackedSizeFactor { get; }

    /// <summary>
    ///     The names of the libraries that are found neither in <paramref name="directory"/> nor anywhere
    ///     the system itself looks for libraries.
    /// </summary>
    /// <remarks>
    ///     The Whisper runtime must never be pointed at CUDA while this list is not empty: with only some
    ///     of the libraries in place it may take the whole process down instead of falling back.
    /// </remarks>
    IReadOnlyList<string> FindMissing(string directory);

    /// <summary>
    ///     Takes the libraries of <paramref name="archive"/> out of the downloaded file into
    ///     <paramref name="stagingDirectory"/>.
    /// </summary>
    /// <exception cref="Echo.Setup.Downloads.DownloadException">Thrown if the file is not what was expected.</exception>
    void Extract(string archivePath, CudaArchive archive, string stagingDirectory);

    /// <summary>
    ///     Moves a library unpacked by <see cref="Extract"/> into <paramref name="directory"/>.
    /// </summary>
    void MoveIntoPlace(string stagingDirectory, string library, string directory);

    /// <summary>
    ///     Makes the libraries in <paramref name="directory"/> visible to the Whisper runtime.
    ///     Must be called before the runtime loads its native library.
    /// </summary>
    void PrepareForLoading(string directory);
}