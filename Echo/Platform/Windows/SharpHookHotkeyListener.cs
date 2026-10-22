using System.Runtime.Versioning;
using Echo.Setup.Hotkeys;
using SharpHook;
using SharpHook.Data;

namespace Echo.Platform.Windows;

/// <summary>
///     Hears the key through a global keyboard hook. Windows lets the hook keep a key from the other programs.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SharpHookHotkeyListener : IHotkeyListener
{
    private readonly SimpleGlobalHook _hook;
    private readonly ILogger<SharpHookHotkeyListener> _logger;

    private KeyCode _key;
    private EventMask? _modifier;

    /// <summary>
    ///     Whether the key is held down: the keyboard repeats the press of a held key, and only the first
    ///     press is reported.
    /// </summary>
    private bool _isDown;

    /// <summary>
    ///     Whether the press of the key was kept from the other programs, so its repeats and its release
    ///     must be kept too.
    /// </summary>
    private bool _isTaken;

    /// <inheritdoc/>
    public event EventHandler<HotkeyEventArgs>? Pressed;

    /// <inheritdoc/>
    public event EventHandler<HotkeyEventArgs>? Released;

    /// <summary>
    ///     The hook keeps hearing the key for as long as Echo runs.
    /// </summary>
    /// <inheritdoc/>
    public event EventHandler? Lost
    {
        add { }
        remove { }
    }

    /// <param name="logger"><see cref="ILogger{SharpHookHotkeyListener}"/></param>
    public SharpHookHotkeyListener(ILogger<SharpHookHotkeyListener> logger)
    {
        _logger = logger;
        _hook = new SimpleGlobalHook();

        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
    }

    /// <inheritdoc/>
    public void Bind(HotkeyChoice hotkey)
    {
        KeyCode? key = KeyMapperService.ParseMainKey(hotkey.Key);
        if (key is null)
            throw new Exception("Couldn't parse push to talk key!");

        _key = key.Value;

        _logger.LogDebug("{Tab}{CheckedEmoji} Main key is set up to [{MainKey}]",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji, hotkey.Key);

        _modifier = string.IsNullOrWhiteSpace(hotkey.Modifier)
            ? null
            : KeyMapperService.ParseModifierKeyOrThrow(hotkey.Modifier);

        if (_modifier is null)
        {
            _logger.LogDebug("{Tab}{CrossEmoji} Modifier key was not set up.",
                LoggerConstants.Tab, LoggerConstants.CrossEmoji);

            return;
        }

        _logger.LogDebug("{Tab}{CheckedEmoji} Modifier key is set up to [{ModifierKey}]",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji, hotkey.Modifier);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("{KeyboardEmoji} Keyboard hook:", LoggerConstants.KeyboardEmoji);

        _logger.LogDebug("{Tab}{LoadingEmoji} Starting global keyboard hook...",
            LoggerConstants.Tab, LoggerConstants.KeyboardEmoji);

        _ = _hook.RunAsync();

        _logger.LogDebug("{Tab}{CheckedEmoji} Global keyboard hook started.",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji);

        return
            Task.CompletedTask;
    }

    /// <summary>
    ///     The hook keeps from the other programs only what its listeners take, and a key given back is taken by
    ///     nobody: it reaches them already.
    /// </summary>
    /// <inheritdoc/>
    public Task ReleaseAsync() => Task.CompletedTask;

    internal void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode != _key)
            return;

        // A repeat of a held key goes where its first press went
        if (_isDown)
        {
            e.SuppressEvent = _isTaken;

            return;
        }

        if (_modifier is not null && !e.RawEvent.Mask.HasFlag(_modifier.Value))
            return;

        var press = new HotkeyEventArgs();

        _isDown = true;

        Pressed?.Invoke(this, press);

        _isTaken = press.IsTaken;
        e.SuppressEvent = press.IsTaken;
    }

    internal void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode != _key || !_isDown)
            return;

        var release = new HotkeyEventArgs();

        _isDown = false;

        Released?.Invoke(this, release);

        // Only the release of a press that was kept from other programs is kept from them too:
        // otherwise a program would see the key go down and never come up
        e.SuppressEvent = _isTaken && release.IsTaken;
        _isTaken = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _hook.Dispose();
    }
}