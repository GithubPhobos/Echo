namespace Echo.Setup.Hardware;

/// <summary>
///     One archive from the NVIDIA redistributable server and the files needed out of it.
/// </summary>
/// <param name="RelativePath">The path of the archive under <see cref="CudaRuntime.DownloadRoot"/>.</param>
/// <param name="SizeBytes">The size of the archive.</param>
/// <param name="Sha256">The checksum from the NVIDIA manifest.</param>
/// <param name="Libraries">The library files to take out of the archive.</param>
internal sealed record CudaArchive(string RelativePath, long SizeBytes, string Sha256, IReadOnlyList<string> Libraries);