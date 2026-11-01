namespace Echo.Platform.Contracts;

/// <summary>
///     What the setup wizard has to ask the desktop for, on desktops where a program cannot simply take it
///     (Linux with Wayland): the push-to-talk key and the permission to press keys for pasting. Elsewhere there is
///     nothing to ask, and the wizard picks the key itself.
/// </summary>
internal interface IDesktopAccess
{
    /// <summary>
    ///     Whether the desktop assigns the push-to-talk key itself, in a window of its own: Echo cannot hear the
    ///     keyboard to let the user press the key.
    /// </summary>
    bool AssignsKeys { get; }

    /// <summary>
    ///     Asks the desktop for the push-to-talk key. The desktop shows its window, where the user confirms
    ///     the suggested key or picks another one.
    /// </summary>
    /// <param name="preferred">The key Echo suggests.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    /// <returns>How the desktop describes the key it gave, or null when it gave none.</returns>
    Task<string?> AssignKeyAsync(HotkeyChoice preferred, CancellationToken cancellationToken);

    /// <summary>
    ///     Whether pressing keys for pasting needs a permission the user gives in a window of the desktop.
    /// </summary>
    bool AsksToPaste { get; }

    /// <summary>
    ///     Whether the user has given the permission to paste already.
    /// </summary>
    bool MayPaste { get; }

    /// <summary>
    ///     Asks for the permission to paste; the desktop shows its window.
    /// </summary>
    /// <returns>Whether the permission was given.</returns>
    Task<bool> AskToPasteAsync(CancellationToken cancellationToken);
}