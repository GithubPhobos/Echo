namespace Echo.Setup.Check;

/// <summary>
///     The dictation pipeline could not be started with the chosen settings.
/// </summary>
internal sealed class DictationTestException(string message, Exception innerException) : Exception(message, innerException);