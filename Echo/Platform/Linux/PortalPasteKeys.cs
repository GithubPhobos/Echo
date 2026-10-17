using System.Runtime.Versioning;

namespace Echo.Platform.Linux;

/// <summary>
///     Wayland: presses Shift+Insert through the remote control permission of the desktop. Mid-work the desktop
///     is never asked again: without the permission the text stays in the clipboard for the user to paste.
/// </summary>
/// <param name="portal">The permission windows of the desktop.</param>
[SupportedOSPlatform("linux")]
internal sealed class PortalPasteKeys(DesktopPortal portal) : IPasteKeys
{
    /// <summary>
    ///     The keysyms of the left Shift and of Insert.
    /// </summary>
    private const uint ShiftKeysym = 0xffe1;
    private const uint InsertKeysym = 0xff63;

    private readonly DesktopPortal _portal = portal;

    /// <inheritdoc/>
    public async Task<bool> PasteAsync() =>
        await _portal.EnsureKeyboardAsync(mayAsk: false, CancellationToken.None)
        && await _portal.PressAsync(ShiftKeysym, InsertKeysym);
}