namespace Echo.Platform.Contracts;

/// <summary>
///     Presses the keys that paste the clipboard into the window in front, as if the user did.
/// </summary>
internal interface IPasteKeys
{
    /// <summary>
    ///     Presses the keys. Returns false when the system does not let Echo press keys, so the user has
    ///     to paste the text by hand.
    /// </summary>
    Task<bool> PasteAsync();
}