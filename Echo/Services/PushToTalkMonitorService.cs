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

    /// <summary>
    ///     Guards everything below: the key comes on the thread of the listener, and a loss or the giving back of
    ///     the key may come on another.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The notices of a recording, told one after another in the order the key gave them. They are told off the
    ///     thread of the listener, which is not to be kept waiting, and none may overtake the one before it: a loss
    ///     told before its press would leave that press recording for good.
    /// </summary>
    private Task _notices = Task.CompletedTask;

    private bool _isRecording;

    /// <summary>
    ///     Whether the key has been given back for good: see <see cref="ReleaseAsync"/>.
    /// </summary>
    private bool _isReleased;

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
    ///     The system stops giving Echo the key, which may then never come up for Echo: a recording under way is
    ///     dropped as if the key were lost.
    /// </summary>
    /// <inheritdoc/>
    public Task ReleaseAsync()
    {
        lock (_lock)
        {
            _isReleased = true;
            Drop();
        }

        // Outside the lock: the listener may wait for its own thread, which may be waiting for the lock
        return _listener.ReleaseAsync();
    }

    /// <summary>
    ///     Holding the key records: the press starts a recording.
    /// </summary>
    internal void OnKeyPressed(object? sender, HotkeyEventArgs e)
    {
        lock (_lock)
        {
            // Paused or given back, the key does what it does without Echo; a recording under way when Echo paused still ends normally
            if ((_activity?.IsPaused == true || _isReleased) && !_isRecording)
            {
                e.IsTaken = false;

                return;
            }

            if (_isRecording)
                return;

            _isRecording = true;

            if (_pushToTalkSettings.PlaySound)
                Task.Run(_soundPlayService.PlayMicrophoneOnSoundAsync);

            Tell(() => OnRecordingTriggered?.Invoke(this, EventArgs.Empty));
        }
    }

    /// <summary>
    ///     Letting the key go ends the recording.
    /// </summary>
    internal void OnKeyReleased(object? sender, HotkeyEventArgs e)
    {
        lock (_lock)
        {
            if (!_isRecording)
            {
                e.IsTaken = false;

                return;
            }

            if (_pushToTalkSettings.PlaySound)
                Task.Run(_soundPlayService.PlayMicrophoneOffSoundAsync);

            Tell(() => OnRecordingReleased?.Invoke(this, EventArgs.Empty));

            _isRecording = false;
        }
    }

    /// <summary>
    ///     The key stopped reaching Echo: see <see cref="Drop"/>.
    /// </summary>
    internal void OnKeyLost(object? sender, EventArgs e)
    {
        lock (_lock)
            Drop();
    }

    /// <summary>
    ///     A recording under way is dropped rather than ended, since the key that would end it will not come up,
    ///     and what it holds is not typed anywhere unexpected.
    /// </summary>
    private void Drop()
    {
        if (!_isRecording)
            return;

        _isRecording = false;

        Tell(() => OnRecordingCancelled?.Invoke(this, EventArgs.Empty));
    }

    /// <summary>
    ///     Tells a notice after those told before it.
    /// </summary>
    private void Tell(Action notice) =>
        _notices = _notices.ContinueWith(_ => notice(), TaskScheduler.Default);

    /// <inheritdoc/>
    public void Dispose()
    {
        // The listener belongs to the services, which close it themselves
        _listener.Pressed -= OnKeyPressed;
        _listener.Released -= OnKeyReleased;
        _listener.Lost -= OnKeyLost;
    }
}