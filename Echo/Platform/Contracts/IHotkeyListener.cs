using Echo.Setup.Hotkeys;

namespace Echo.Platform.Contracts;

/// <summary>
///     Hears the push-to-talk key anywhere in the system and says when it goes down and comes up.
///     What a press means (start a recording, stop it) is decided by whoever listens.
/// </summary>
internal interface IHotkeyListener : IDisposable
{
    /// <summary>
    ///     The key went down. A key held down is reported once, not on every repeat of the keyboard.
    /// </summary>
    event EventHandler<HotkeyEventArgs>? Pressed;

    /// <summary>
    ///     The key came up.
    /// </summary>
    event EventHandler<HotkeyEventArgs>? Released;

    /// <summary>
    ///     The key stopped reaching Echo, as when the desktop takes it back: a key held down will not come up.
    /// </summary>
    event EventHandler? Lost;

    /// <summary>
    ///     Sets the key to listen to.
    /// </summary>
    /// <param name="hotkey">The key and its modifier, as the settings hold them.</param>
    /// <exception cref="Exception">Thrown if the key cannot be listened to on this system.</exception>
    void Bind(HotkeyChoice hotkey);

    /// <summary>
    ///     Starts listening in the background.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting, as when Echo stops while the desktop still asks the user.</param>
    Task StartAsync(CancellationToken cancellationToken);
}