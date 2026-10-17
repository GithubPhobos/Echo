using System.Runtime.Versioning;
using Echo.Setup.Hotkeys;

namespace Echo.Platform.Linux;

/// <summary>
///     Wayland: the desktop holds the key and tells Echo when it goes down and comes up. The key never reaches
///     other programs; the desktop keeps it, even while Echo is paused.
/// </summary>
/// <param name="portal">The permission windows of the desktop.</param>
/// <param name="logger"><see cref="ILogger{PortalHotkeyListener}"/></param>
/// <param name="settings">Keeps the description of the key when the user changes it in the desktop's settings.</param>
[SupportedOSPlatform("linux")]
internal sealed class PortalHotkeyListener(DesktopPortal portal, ILogger<PortalHotkeyListener> logger, IUserSettingsStore settings) : IHotkeyListener
{
    private readonly DesktopPortal _portal = portal;
    private readonly ILogger<PortalHotkeyListener> _logger = logger;
    private readonly IUserSettingsStore _settings = settings;

    private string? _trigger;
    private bool _isListening;

    /// <inheritdoc/>
    public event EventHandler<HotkeyEventArgs>? Pressed;

    /// <inheritdoc/>
    public event EventHandler<HotkeyEventArgs>? Released;

    /// <summary>
    ///     The key of the settings is only Echo's suggestion: the desktop may have given another one.
    /// </summary>
    public void Bind(HotkeyChoice hotkey) =>
        _trigger = DesktopPortal.ToTrigger(hotkey) ?? DesktopPortal.ToTrigger(HotkeyChoice.Default);

    /// <summary>
    ///     Finds the key the desktop gave Echo. Without one the desktop asks the user now, in its window.
    /// </summary>
    public async Task StartAsync()
    {
        _logger.LogDebug("{KeyboardEmoji} Keyboard (Wayland, through the desktop):", LoggerConstants.KeyboardEmoji);

        if (await _portal.VersionAsync(DesktopPortal.ShortcutsInterface) is null)
        {
            throw new Exception(
                "This desktop does not give programs keys of their own (no GlobalShortcuts portal), so the push-to-talk key " +
                "is not available here. GNOME 48 or newer and KDE Plasma have it.");
        }

        string label = await _portal.EnsureShortcutAsync(_trigger!, PortalDesktopAccess.SpeakDescription, askAgain: false, CancellationToken.None)
            ?? throw new Exception("The desktop gave no push-to-talk key. Run 'echooo setup' to ask for it again.");

        _portal.ShortcutActivated += OnActivated;
        _portal.ShortcutDeactivated += OnDeactivated;
        _portal.ShortcutChanged += OnChanged;
        _isListening = true;

        Remember(label);

        _logger.LogDebug("{Tab}{CheckedEmoji} The desktop gave the key [{Key}].",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji, label);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_isListening)
            return;

        // The portal and its session belong to the whole process; only this listener stops listening
        _portal.ShortcutActivated -= OnActivated;
        _portal.ShortcutDeactivated -= OnDeactivated;
        _portal.ShortcutChanged -= OnChanged;
        _isListening = false;
    }

    private void OnActivated(object? sender, string shortcut)
    {
        if (shortcut == DesktopPortal.SpeakShortcut)
            Pressed?.Invoke(this, new HotkeyEventArgs());
    }

    private void OnDeactivated(object? sender, string shortcut)
    {
        if (shortcut == DesktopPortal.SpeakShortcut)
            Released?.Invoke(this, new HotkeyEventArgs());
    }

    private void OnChanged(object? sender, string label)
    {
        _logger.LogInformation("{Tab}{KeyboardEmoji} The push-to-talk key is now [{Key}].",
            LoggerConstants.Tab, LoggerConstants.KeyboardEmoji, label);

        Remember(label);
    }

    /// <summary>
    ///     Keeps the description of the key for the screens that name it.
    /// </summary>
    private void Remember(string label)
    {
        if (_settings.ReadValue(nameof(PushToTalkSettings), nameof(PushToTalkSettings.DesktopShortcut)) != label)
            _settings.WriteValue(nameof(PushToTalkSettings), nameof(PushToTalkSettings.DesktopShortcut), label);
    }
}