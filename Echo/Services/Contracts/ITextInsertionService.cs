namespace Echo.Services.Contracts;

/// <summary>
///     Handles programmatic keyboard simulations and clipboard management 
///     to seamlessly insert recognized text into the active user window.
/// </summary>
internal interface ITextInsertionService
{
    /// <summary>
    ///     Copies the recognized text to the system clipboard and simulates a 'Ctrl+V' 
    ///     keystroke to paste it at the current cursor position.
    /// </summary>
    /// <param name="text">The transcribed text to insert.</param>
    Task InsertTextAsync(string? text);
}