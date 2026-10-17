namespace Echo.Setup.Downloads;

/// <summary>
///     A file to download together with what is known about it in advance.
/// </summary>
/// <param name="Url">Where the file is.</param>
/// <param name="SizeBytes">The expected size.</param>
/// <param name="Sha256">The expected checksum, in hexadecimal.</param>
internal sealed record DownloadRequest(Uri Url, long SizeBytes, string Sha256);