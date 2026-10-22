namespace Echo.Services.Contracts;

/// <summary>
///     Handles programmatic keyboard simulations and clipboard management 
///     to seamlessly insert recognized text into the active user window.
/// </summary>
internal interface ITextInsertionService
{
    /// <summary>
    ///     Gets ready before the first phrase: a permission to paste that has to be renewed is renewed when Echo
    ///     starts, not in the middle of dictation.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting, as when Echo stops while the desktop still asks the user.</param>
    Task PrepareAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Copies the recognized text to the system clipboard and simulates a 'Ctrl+V' 
    ///     keystroke to paste it at the current cursor position.
    /// </summary>
    /// <param name="text">The transcribed text to insert.</param>
    Task InsertTextAsync(string? text);

    /// <summary>
    ///     Puts a text into the clipboard without pasting it, as when the user asks for a phrase recognized earlier.
    ///     A phrase being pasted meanwhile is pasted first: the clipboard holds it until its keys are pressed.
    /// </summary>
    /// <param name="text">The text to leave in the clipboard.</param>
    Task CopyAsync(string text);
}