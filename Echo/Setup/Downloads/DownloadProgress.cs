namespace Echo.Setup.Downloads;

/// <summary>
///     How far a download has got.
/// </summary>
/// <param name="BytesDone">Bytes already on disk.</param>
/// <param name="BytesTotal">The size of the whole file.</param>
/// <param name="BytesPerSecond">The current speed; 0 while the file is being verified.</param>
/// <param name="IsVerifying">Whether the download has finished and its checksum is being checked.</param>
internal readonly record struct DownloadProgress(long BytesDone, long BytesTotal, double BytesPerSecond, bool IsVerifying);