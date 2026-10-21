namespace Echo.Platform.Contracts;

/// <summary>
///     Presses the keys that paste the clipboard into the window in front, as if the user did.
/// </summary>
internal interface IPasteKeys
{
    /// <summary>
    ///     Gets ready before the first phrase: a permission to press keys that has to be renewed is renewed now, when
    ///     Echo starts, not in the middle of dictation.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting, as when Echo stops while the desktop still asks the user.</param>
    Task PrepareAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Presses the keys. Returns false when the system does not let Echo press keys, so the user has
    ///     to paste the text by hand.
    /// </summary>
    Task<bool> PasteAsync();
}