namespace Echo.Listening;

/// <inheritdoc cref="IListenActivity"/>
/// <param name="log">The screen's log.</param>
/// <param name="isEchoFocused">Tells whether Echo's window has the keyboard.</param>
internal sealed class ListenActivity(ScreenLog log, Func<bool> isEchoFocused) : IListenActivity
{
    private const int KeptLevels = 64;

    private readonly object _lock = new();
    private readonly Queue<float> _levels = new(Enumerable.Repeat(0f, KeptLevels));
    private readonly Func<bool> _isEchoFocused = isEchoFocused;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ListenState _state = ListenState.Starting;
    private DateTimeOffset _recordingStarted;
    private volatile bool _isPaused;

    /// <inheritdoc/>
    public ListenState State
    {
        get
        {
            lock (_lock)
                return _state;
        }
    }

    /// <inheritdoc/>
    public Task Started => _started.Task;

    /// <inheritdoc/>
    public DateTimeOffset RecordingStarted
    {
        get
        {
            lock (_lock)
                return _recordingStarted;
        }
    }

    /// <inheritdoc/>
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            _isPaused = value;

            lock (_lock)
            {
                // A recording that is already under way finishes as usual; otherwise the state follows at once
                if (_state is ListenState.Listening or ListenState.Paused)
                    _state = value ? ListenState.Paused : ListenState.Listening;
            }
        }
    }

    /// <inheritdoc/>
    public string? Runtime { get; set; }

    /// <inheritdoc/>
    public ScreenLog Log { get; } = log;

    /// <inheritdoc/>
    public void SetState(ListenState state)
    {
        lock (_lock)
        {
            if (state == ListenState.Recording && _state != ListenState.Recording)
                _recordingStarted = DateTimeOffset.Now;

            // Back from a recording to waiting means "paused" while the user has paused Echo
            _state = state == ListenState.Listening && _isPaused ? ListenState.Paused : state;
        }

        // Only a finished start-up waits for the key or fails; a phrase said while the model loads is recorded
        // and recognized before Echo waits for the key
        if (state is ListenState.Listening or ListenState.Failed)
            _started.TrySetResult();
    }

    /// <inheritdoc/>
    public void ReportLevel(float level)
    {
        lock (_lock)
        {
            _levels.Dequeue();
            _levels.Enqueue(level);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<float> RecentLevels(int count)
    {
        lock (_lock)
            return [.. _levels.Skip(Math.Max(0, _levels.Count - count))];
    }

    /// <inheritdoc/>
    public bool IsEchoFocused() => _isEchoFocused();
}