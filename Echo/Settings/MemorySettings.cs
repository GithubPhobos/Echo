namespace Echo.Settings;

/// <summary>
///     The dictation history: every recognized phrase kept on this computer, one file a month.
/// </summary>
internal sealed class MemorySettings
{
    /// <summary>
    ///     Whether the recognized phrases are kept.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
