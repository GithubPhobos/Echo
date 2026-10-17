using SharpHook;
using SharpHook.Data;

namespace Echo.Services;

/// <inheritdoc cref="IPushToTalkMonitorService"/>
internal sealed class PushToTalkMonitorService : IPushToTalkMonitorService
{
    private readonly SimpleGlobalHook _hook;
    private readonly ISoundPlayService _soundPlayService;
    private readonly PushToTalkSettings _pushToTalkSettings;
    private readonly ILogger<PushToTalkMonitorService> _logger;
    private readonly IListenActivity? _activity;

    private KeyCode _key;
    private bool _isRecording;
    private EventMask? _modifier;
    private bool _hasModifierSetUp;

    /// <inheritdoc/>
    public event EventHandler? OnRecordingReleased;

    /// <inheritdoc/>
    public event EventHandler? OnRecordingTriggered;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PushToTalkMonitorService"/>.
    /// </summary>
    /// <param name="soundPlayService"><see cref="ISoundPlayService"/></param>
    /// <param name="logger"><see cref="ILogger{PushToTalkMonitorService}"/></param>
    /// <param name="pushToTalkSettingsOptions"><see cref="IOptions{PushToTalkSettings}"/></param>
    /// <param name="activity">The working screen, when Echo runs with one: it can pause listening to the key.</param>
    public PushToTalkMonitorService(ISoundPlayService soundPlayService,
                                    ILogger<PushToTalkMonitorService> logger,
                                    IOptions<PushToTalkSettings> pushToTalkSettingsOptions,
                                    IListenActivity? activity = null)
    {
        _logger = logger;
        _activity = activity;
        _hook = new SimpleGlobalHook();
        _soundPlayService = soundPlayService;
        _pushToTalkSettings = pushToTalkSettingsOptions.Value;

        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
    }

    /// <inheritdoc/>
    public Task StartListeningAsync()
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

    /// <inheritdoc/>
    public void InitializePushToTalkHotkeysOrThrow()
    {
        _logger.LogDebug("{PushToTalkEmoji} Push-to-talk hotkeys:", LoggerConstants.PushToTalkEmoji);

        KeyCode? key = KeyMapperService.ParseMainKey(_pushToTalkSettings.Key);
        if (key is null)
            throw new Exception("Couldn't parse push to talk key!");

        _key = key.Value;

        _logger.LogDebug("{Tab}{CheckedEmoji} Main key is set up to [{MainKey}]",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji, _pushToTalkSettings.Key);

        if (string.IsNullOrWhiteSpace(_pushToTalkSettings.Modifier)
            ||
            (
                KeyMapperService.ParseModifierKeyOrThrow(_pushToTalkSettings.Modifier) is var modifier
                &&
                modifier is null)
            )
        {
            _logger.LogDebug("{Tab}{CrossEmoji} Modifier key was not set up.",
                LoggerConstants.Tab, LoggerConstants.CrossEmoji);

            return;
        }

        _modifier = modifier;
        _hasModifierSetUp = true;

        _logger.LogDebug("{Tab}{CheckedEmoji} Modifier key is set up to [{ModifierKey}]",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji, _pushToTalkSettings.Modifier);
    }

    internal void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        // Paused, the key does what it does without Echo, and a recording already under way still ends normally
        if (_activity?.IsPaused == true && !_isRecording)
            return;

        if (e.Data.KeyCode == _key
            &&
            (
                !_hasModifierSetUp
                ||
                (
                    _hasModifierSetUp
                    &&
                    e.RawEvent.Mask.HasFlag(_modifier!.Value))
                )
            )
        {
            e.SuppressEvent = true;

            if (!_isRecording)
            {
                _isRecording = true;

                if (_pushToTalkSettings.PlaySound)
                    Task.Run(_soundPlayService.PlayMicrophoneOnSoundAsync);

                Task.Run(() => OnRecordingTriggered?.Invoke(this, EventArgs.Empty));
            }
        }
    }

    internal void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        // Only the release of a press that was kept from other programs is kept from them too:
        // otherwise a program would see the key go down and never come up
        if (e.Data.KeyCode == _key && _isRecording)
        {
            e.SuppressEvent = true;

            if (_pushToTalkSettings.PlaySound)
                Task.Run(_soundPlayService.PlayMicrophoneOffSoundAsync);

            Task.Run(() => OnRecordingReleased?.Invoke(this, EventArgs.Empty));

            _isRecording = false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _hook.Dispose();
    }
}