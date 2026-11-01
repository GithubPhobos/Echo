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
    /// <summary>
    ///     How often, and how far apart, Echo looks for its key again once the desktop has lost it: a restarted
    ///     portal service takes a moment to come back.
    /// </summary>
    private const int RestoreAttempts = 5;
    private static readonly TimeSpan _restorePause = TimeSpan.FromSeconds(1);

    private readonly DesktopPortal _portal = portal;
    private readonly ILogger<PortalHotkeyListener> _logger = logger;
    private readonly IUserSettingsStore _settings = settings;

    private string? _trigger;
    private bool _isListening;
    private CancellationTokenSource? _stopping;

    /// <inheritdoc/>
    public event EventHandler<HotkeyEventArgs>? Pressed;

    /// <inheritdoc/>
    public event EventHandler<HotkeyEventArgs>? Released;

    /// <inheritdoc/>
    public event EventHandler? Lost;

    /// <summary>
    ///     The key of the settings is only Echo's suggestion: the desktop may have given another one.
    /// </summary>
    public void Bind(HotkeyChoice hotkey) =>
        _trigger = DesktopPortal.ToTrigger(hotkey) ?? DesktopPortal.ToTrigger(HotkeyChoice.Default);

    /// <summary>
    ///     Finds the key the desktop gave Echo. Without one the desktop asks the user now, in its window; stopping
    ///     Echo meanwhile closes the window.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("{KeyboardEmoji} Keyboard (Wayland, through the desktop):", LoggerConstants.KeyboardEmoji);

        if (await _portal.VersionAsync(DesktopPortal.ShortcutsInterface) is null)
        {
            throw new Exception(
                "This desktop does not give programs keys of their own (no GlobalShortcuts portal), so the push-to-talk key " +
                "is not available here. GNOME 48 or newer and KDE Plasma have it.");
        }

        string label = await _portal.EnsureShortcutAsync(_trigger!, PortalDesktopAccess.SpeakDescription, askAgain: false, cancellationToken)
            ?? throw new Exception("The desktop gave no push-to-talk key. Run 'echooo setup' to ask for it again.");

        _portal.ShortcutActivated += OnActivated;
        _portal.ShortcutDeactivated += OnDeactivated;
        _portal.ShortcutChanged += OnChanged;
        _portal.ShortcutsLost += OnLost;
        _stopping = new CancellationTokenSource();
        _isListening = true;

        Remember(label);

        _logger.LogDebug("{Tab}{CheckedEmoji} The desktop gave the key [{Key}].",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji, label);
    }

    /// <summary>
    ///     Stops listening and closes the session of the key, so the desktop gives the key to other programs again.
    ///     The desktop keeps the key it gave Echo: the next start or the setup wizard finds it again without the window.
    /// </summary>
    /// <inheritdoc/>
    public Task ReleaseAsync()
    {
        Dispose();

        return _portal.CloseShortcutsAsync();
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
        _portal.ShortcutsLost -= OnLost;
        _isListening = false;

        _stopping?.Cancel();
        _stopping?.Dispose();
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

    /// <summary>
    ///     The desktop lost the key: a phrase under way ends without its key coming up, and the key is looked for
    ///     again in the background.
    /// </summary>
    private void OnLost(object? sender, ShortcutsLostEventArgs e)
    {
        _logger.LogWarning("{WarningEmoji} The push-to-talk key stopped working: {Reason}.",
            LoggerConstants.WarningEmoji, e.Reason);

        Lost?.Invoke(this, EventArgs.Empty);

        if (_stopping is { } stopping)
            _ = RestoreAsync(e.IsServiceGone, stopping.Token);
    }

    /// <summary>
    ///     Looks for the key again a few times, without the desktop's window: a key the user took back is asked for
    ///     only in the setup wizard. One the desktop keeps for a portal service that went away is not given again
    ///     on GNOME, by /setup or a new start of Echo alike: logging in again is what brings it back.
    /// </summary>
    /// <param name="isServiceGone">Whether the key went with the portal service, rather than with its session.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    private async Task RestoreAsync(bool isServiceGone, CancellationToken cancellationToken)
    {
        try
        {
            for (int attempt = 1; attempt <= RestoreAttempts; attempt++)
            {
                await Task.Delay(_restorePause, cancellationToken);

                if (await _portal.RestoreShortcutAsync(cancellationToken) is { } label)
                {
                    _logger.LogInformation("{Tab}{CheckedEmoji} The push-to-talk key works again: [{Key}].",
                        LoggerConstants.Tab, LoggerConstants.CheckedEmoji, label);

                    Remember(label);

                    return;
                }
            }

            if (isServiceGone)
            {
                _logger.LogWarning("{Tab}{WarningEmoji} The desktop no longer passes the push-to-talk key to Echo since its portal service stopped. Log out and log in again to get it back.",
                    LoggerConstants.Tab, LoggerConstants.WarningEmoji);
            }
            else
            {
                _logger.LogWarning("{Tab}{WarningEmoji} The desktop no longer gives Echo the push-to-talk key. Ask for it again with /setup.",
                    LoggerConstants.Tab, LoggerConstants.WarningEmoji);
            }
        }
        catch (OperationCanceledException)
        {
            // Echo stops
        }
        catch (Exception exc)
        {
            _logger.LogWarning("{Tab}{WarningEmoji} Couldn't get the push-to-talk key back: {ErrorMessage}",
                LoggerConstants.Tab, LoggerConstants.WarningEmoji, exc.Message);
        }
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