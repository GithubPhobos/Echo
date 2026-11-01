namespace Echo.Platform;

/// <summary>
///     For systems where Echo hears the key and presses keys without asking: Windows, and Linux with X11.
/// </summary>
internal sealed class NoDesktopAccess : IDesktopAccess
{
    /// <inheritdoc/>
    public bool AssignsKeys => false;

    /// <inheritdoc/>
    public bool AsksToPaste => false;

    /// <inheritdoc/>
    public bool MayPaste => true;

    /// <inheritdoc/>
    public Task<string?> AssignKeyAsync(HotkeyChoice preferred, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public Task<bool> AskToPasteAsync(CancellationToken cancellationToken) =>
        Task.FromResult(true);
}