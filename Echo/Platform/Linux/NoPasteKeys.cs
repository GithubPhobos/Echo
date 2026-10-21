namespace Echo.Platform.Linux;

/// <summary>
///     For sessions where Echo cannot press keys: the text stays in the clipboard for the user to paste.
/// </summary>
internal sealed class NoPasteKeys : IPasteKeys
{
    /// <inheritdoc/>
    public Task PrepareAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<bool> PasteAsync() => Task.FromResult(false);
}