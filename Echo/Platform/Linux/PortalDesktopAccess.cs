namespace Echo.Platform.Linux;

/// <summary>
///     Wayland: the key and the permission to paste come from the permission windows of the desktop.
/// </summary>
/// <param name="portal">The permission windows of the desktop.</param>
[SupportedOSPlatform("linux")]
internal sealed class PortalDesktopAccess(DesktopPortal portal) : IDesktopAccess
{
    /// <summary>
    ///     What the key does, as the desktop shows it in its window and its keyboard settings: in the language of
    ///     Echo's screens, since the user reads it there.
    /// </summary>
    internal static string SpeakDescription => Strings.DesktopShortcutDescription;

    private readonly DesktopPortal _portal = portal;

    /// <inheritdoc/>
    public bool AssignsKeys => true;

    /// <inheritdoc/>
    public bool AsksToPaste => true;

    /// <inheritdoc/>
    public bool MayPaste => _portal.HasKeyboardPermission;

    /// <inheritdoc/>
    public Task<string?> AssignKeyAsync(HotkeyChoice preferred, CancellationToken cancellationToken) =>
        _portal.EnsureShortcutAsync(
            DesktopPortal.ToTrigger(preferred) ?? DesktopPortal.ToTrigger(HotkeyChoice.Default)!,
            SpeakDescription,
            askAgain: true,
            cancellationToken);

    /// <inheritdoc/>
    public Task<bool> AskToPasteAsync(CancellationToken cancellationToken) =>
        _portal.EnsureKeyboardAsync(mayAsk: true, cancellationToken);
}