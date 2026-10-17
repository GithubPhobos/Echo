namespace Echo.Platform.Contracts;

/// <summary>
///     The clipboard of the system, as far as Echo needs it: to leave the recognized text there.
/// </summary>
internal interface ITextClipboard
{
    /// <summary>
    ///     Puts the text into the clipboard, and on systems that have one, into the selection as well.
    /// </summary>
    Task SetTextAsync(string text);
}