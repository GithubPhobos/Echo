namespace Echo.Settings;

/// <summary>
///    Text insertion settings. 
/// </summary>
internal sealed class TextInsertionSettings
{
    /// <summary>
    ///     Indicates whether after AI recognition, text will be auto insertedunder caret.
    /// </summary>
    /// <remarks>
    ///      If set to false it still will be saved to clipboard and you can Ctrl-V manually.
    /// </remarks>
    public bool UseAutoInsert { get; init; }
}