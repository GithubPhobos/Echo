namespace Echo.Tests.UnitTests;

/// <summary>Drives one-shot download timers without depending on thread-pool scheduling or wall-clock delays.</summary>
internal sealed class DownloadClock : TimeProvider
{
    private readonly List<Action> _timers = [];

    /// <summary>The simulated time spent receiving response pieces.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <inheritdoc/>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc/>
    public override long GetTimestamp() => Elapsed.Ticks;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + Elapsed;

    /// <summary>Moves all active timers forward, so failure to reset a stall timer is observable.</summary>
    public void Advance(TimeSpan elapsed)
    {
        Elapsed += elapsed;
        foreach (Action timer in _timers.ToArray())
            timer();
    }

    /// <inheritdoc/>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        TimeSpan? due = null;
        bool disposed = false;
        var timer = new Mock<ITimer>();
        timer.Setup(t => t.Change(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns((TimeSpan next, TimeSpan repeat) =>
            {
                Assert.Equal(Timeout.InfiniteTimeSpan, repeat);
                due = next == Timeout.InfiniteTimeSpan ? null : Elapsed + next;
                return !disposed;
            });
        timer.Setup(t => t.Dispose()).Callback(() => disposed = true);
        timer.Setup(t => t.DisposeAsync()).Returns(() => { disposed = true; return ValueTask.CompletedTask; });
        timer.Object.Change(dueTime, period);
        _timers.Add(() =>
        {
            if (!disposed && due is { } deadline && Elapsed >= deadline)
            {
                due = null;
                callback(state);
            }
        });
        return timer.Object;
    }
}