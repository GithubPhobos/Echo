namespace Echo.Setup.Downloads;

/// <summary>
///     The download did not produce the expected file.
/// </summary>
internal sealed class DownloadException(string message, Exception? innerException = null) : Exception(message, innerException);