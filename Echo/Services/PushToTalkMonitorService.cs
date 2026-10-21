using Echo.Setup.Hotkeys;

namespace Echo.Services;

/// <inheritdoc cref="IPushToTalkMonitorService"/>
internal sealed class PushToTalkMonitorService : IPushToTalkMonitorService
{
    private readonly IHotkeyListener _listener;
    private readonly ISoundPlayService _soundPlayService;
    private readonly PushToTalkSettings _pushToTalkSettings;
    private readonly ILogger<PushToTalkMonitorService> _logger;
    private readonly IListenActivity? _activity;

    private bool _isRecording;

    /// <inheritdoc/>
    public event EventHandler? OnRecordingReleased;

    /// <inheritdoc/>
    public event EventHandler? OnRecordingTriggered;

    /// <inheritdoc/>
    public event EventHandler? OnRecordingCancelled;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PushToTalkMonitorService"/>.
    /// </summary>
    /// <param name="listener">Hears the key in the way the system allows.</param>
    /// <param name="soundPlayService"><see cref="ISoundPlayService"/></param>
    /// <param name="logger"><see cref="ILogger{PushToTalkMonitorService}"/></param>
    /// <param name="pushToTalkSettingsOptions"><see cref="IOptions{PushToTalkSettings}"/></param>
    /// <param name="activity">The working screen, when Echo runs with one: it can pause listening to the key.</param>
    public PushToTalkMonitorService(IHotkeyListener listener,
                                    ISoundPlayService soundPlayService,
                                    ILogger<PushToTalkMonitorService> logger,
                                    IOptions<PushToTalkSettings> pushToTalkSettingsOptions,
                                    IListenActivity? activity = null)
    {
        _logger = logger;
        _activity = activity;
        _listener = listener;
        _soundPlayService = soundPlayService;
        _pushToTalkSettings = pushToTalkSettingsOptions.Value;

        _listener.Pressed += OnKeyPressed;
        _listener.Released += OnKeyReleased;
        _listener.Lost += OnKeyLost;
    }

    /// <inheritdoc/>
    public Task StartListeningAsync(CancellationToken cancellationToken) =>
        _listener.StartAsync(cancellationToken);

    /// <inheritdoc/>
    public void InitializePushToTalkHotkeysOrThrow()
    {
        _logger.LogDebug("{PushToTalkEmoji} Push-to-talk hotkeys:", LoggerConstants.PushToTalkEmoji);

        _listener.Bind(new HotkeyChoice(_pushToTalkSettings.Key, _pushToTalkSettings.Modifier ?? string.Empty));
    }

    /// <summary>
    ///     Holding the key records: the press starts a recording.
    /// </summary>
    internal void OnKeyPressed(object? sender, HotkeyEventArgs e)
    {
        // Paused, the key does what it does without Echo, and a recording already under way still ends normally
        if (_activity?.IsPaused == true && !_isRecording)
        {
            e.IsTaken = false;

            return;
        }

        if (_isRecording)
            return;

        _isRecording = true;

        if (_pushToTalkSettings.PlaySound)
            Task.Run(_soundPlayService.PlayMicrophoneOnSoundAsync);

        Task.Run(() => OnRecordingTriggered?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    ///     Letting the key go ends the recording.
    /// </summary>
    internal void OnKeyReleased(object? sender, HotkeyEventArgs e)
    {
        if (!_isRecording)
        {
            e.IsTaken = false;

            return;
        }

        if (_pushToTalkSettings.PlaySound)
            Task.Run(_soundPlayService.PlayMicrophoneOffSoundAsync);

        Task.Run(() => OnRecordingReleased?.Invoke(this, EventArgs.Empty));

        _isRecording = false;
    }

    /// <summary>
    ///     The key stopped reaching Echo: a recording under way is dropped rather than ended, since the key
    ///     that would end it will not come up, and what it holds is not typed anywhere unexpected.
    /// </summary>
    internal void OnKeyLost(object? sender, EventArgs e)
    {
        if (!_isRecording)
            return;

        _isRecording = false;

        Task.Run(() => OnRecordingCancelled?.Invoke(this, EventArgs.Empty));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The listener belongs to the services, which close it themselves
        _listener.Pressed -= OnKeyPressed;
        _listener.Released -= OnKeyReleased;
        _listener.Lost -= OnKeyLost;
    }
}