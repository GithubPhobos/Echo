using System.Runtime.Versioning;
using Echo.Platform.Windows;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Echo.Tests.UnitTests;

/// <summary>
///     The notices of the Windows audio engine, given the way the engine gives them.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAudioDeviceEventsTests
{
    /// <summary>
    ///     The microphone and the loudspeakers of the USB sound card on the computer of the user.
    /// </summary>
    private const string Microphone = "{0.0.1.00000000}.{d43a8132-2513-425b-b985-b21c0d949294}";
    private const string Loudspeakers = "{0.0.0.00000000}.{93cdb862-92d5-43f1-a653-38e2bcb0c475}";

    /// <summary>
    ///     How long a test waits for a notice of the real audio engine.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly ManualClock _clock = new();

    private int _microphoneChanges;
    private int _loudspeakerChanges;

    [WindowsFact]
    public void Changes_SoundCardComesBackAfterSleep_AreToldOnceItHasSettled()
    {
        // Arrange
        using WindowsAudioDeviceEvents events = Events();
        IMMNotificationClient engine = events;

        // Act: as the system log showed it, the card goes away and comes back, its loudspeakers twice
        engine.OnDeviceStateChanged(Loudspeakers, DeviceState.NotPresent);
        _clock.Advance(TimeSpan.FromSeconds(1));
        engine.OnDeviceStateChanged(Microphone, DeviceState.NotPresent);
        engine.OnDeviceStateChanged(Microphone, DeviceState.Active);
        _clock.Advance(TimeSpan.FromSeconds(1));
        engine.OnDeviceStateChanged(Loudspeakers, DeviceState.Active);
        _clock.Advance(TimeSpan.FromSeconds(1));
        engine.OnDeviceStateChanged(Loudspeakers, DeviceState.NotPresent);
        _clock.Advance(TimeSpan.FromSeconds(1.5));
        engine.OnDeviceStateChanged(Loudspeakers, DeviceState.Active);

        _clock.Advance(WindowsAudioDeviceEvents.SettleTime - TimeSpan.FromMilliseconds(1));
        (int Microphones, int Loudspeakers) toldTooEarly = (_microphoneChanges, _loudspeakerChanges);
        _clock.Advance(TimeSpan.FromMilliseconds(1));

        // Assert
        Assert.Equal((0, 0), toldTooEarly);
        Assert.Equal(1, _microphoneChanges);
        Assert.Equal(1, _loudspeakerChanges);
    }

    [WindowsFact]
    public void MicrophonesChanged_MicrophonePluggedIn_LeavesTheLoudspeakersAlone()
    {
        // Arrange
        using WindowsAudioDeviceEvents events = Events();
        IMMNotificationClient engine = events;

        // Act
        engine.OnDeviceAdded(Microphone);
        _clock.Advance(WindowsAudioDeviceEvents.SettleTime);

        // Assert
        Assert.Equal(1, _microphoneChanges);
        Assert.Equal(0, _loudspeakerChanges);
    }

    [WindowsFact]
    public void LoudspeakersChanged_LoudspeakersRemoved_LeavesTheMicrophoneAlone()
    {
        // Arrange
        using WindowsAudioDeviceEvents events = Events();
        IMMNotificationClient engine = events;

        // Act
        engine.OnDeviceRemoved(Loudspeakers);
        _clock.Advance(WindowsAudioDeviceEvents.SettleTime);

        // Assert
        Assert.Equal(0, _microphoneChanges);
        Assert.Equal(1, _loudspeakerChanges);
    }

    [WindowsFact]
    public void LoudspeakersChanged_SystemPlaysToOtherOnesByDefault_IsRaisedOnce()
    {
        // Arrange
        using WindowsAudioDeviceEvents events = Events();
        IMMNotificationClient engine = events;

        // Act: choosing the default device in Windows sets it for both of these roles
        engine.OnDefaultDeviceChanged(DataFlow.Render, Role.Console, Loudspeakers);
        engine.OnDefaultDeviceChanged(DataFlow.Render, Role.Multimedia, Loudspeakers);
        _clock.Advance(WindowsAudioDeviceEvents.SettleTime);

        // Assert
        Assert.Equal(0, _microphoneChanges);
        Assert.Equal(1, _loudspeakerChanges);
    }

    [WindowsFact]
    public void Changes_NewDefaultForCallsOrRecordingOrANewVolume_AreNotTold()
    {
        // Arrange
        using WindowsAudioDeviceEvents events = Events();
        IMMNotificationClient engine = events;

        // Act: Echo plays to neither the device for calls nor records from the default microphone
        engine.OnDefaultDeviceChanged(DataFlow.Render, Role.Communications, Loudspeakers);
        engine.OnDefaultDeviceChanged(DataFlow.Capture, Role.Console, Microphone);
        engine.OnPropertyValueChanged(Loudspeakers, new PropertyKey(Guid.NewGuid(), 0));
        _clock.Advance(WindowsAudioDeviceEvents.SettleTime);

        // Assert
        Assert.Equal(0, _microphoneChanges);
        Assert.Equal(0, _loudspeakerChanges);
    }

    [WindowsFact]
    public void Changes_DeviceTheSystemDoesNotKnow_CountForBoth()
    {
        // Arrange: a device removed from the system cannot be asked what it was
        using WindowsAudioDeviceEvents events = Events();
        IMMNotificationClient engine = events;

        // Act
        engine.OnDeviceRemoved("{0.0.1.00000000}.{00000000-0000-0000-0000-000000000000}");
        _clock.Advance(WindowsAudioDeviceEvents.SettleTime);

        // Assert
        Assert.Equal(1, _microphoneChanges);
        Assert.Equal(1, _loudspeakerChanges);
    }

    [WindowsFact]
    public void Changes_EchoStoppedBeforeTheDevicesSettled_AreNotTold()
    {
        // Arrange
        WindowsAudioDeviceEvents events = Events();
        IMMNotificationClient engine = events;
        engine.OnDeviceStateChanged(Microphone, DeviceState.Active);

        // Act
        events.Dispose();
        _clock.Advance(WindowsAudioDeviceEvents.SettleTime);

        // Assert
        Assert.Equal(0, _microphoneChanges);
        Assert.Equal(0, _loudspeakerChanges);
    }

    [WindowsFact]
    public async Task Changes_DeviceChangesWhileTheLastOnesAreLookedAt_AreToldOnceAfterTheQuietPeriodOfTheNewOne()
    {
        // Arrange: Windows is slow to say what the first microphone is
        using var lookingAtFirst = new ManualResetEventSlim();
        using var firstLookedAt = new ManualResetEventSlim();

        using WindowsAudioDeviceEvents events = Events(deviceId =>
        {
            if (deviceId == "first microphone")
            {
                lookingAtFirst.Set();
                firstLookedAt.Wait(_patience);
            }

            return DataFlow.Capture;
        });

        IMMNotificationClient engine = events;
        engine.OnDeviceAdded("first microphone");

        Task telling = Task.Run(() => _clock.Advance(WindowsAudioDeviceEvents.SettleTime));
        Assert.True(lookingAtFirst.Wait(_patience));

        // Act: another microphone comes while the first is looked at
        engine.OnDeviceAdded("second microphone");
        firstLookedAt.Set();
        await telling.WaitAsync(_patience);
        int toldTooEarly = _microphoneChanges;

        _clock.Advance(WindowsAudioDeviceEvents.SettleTime);

        // Assert: told once for both, when the devices were quiet after the second
        Assert.Equal(0, toldTooEarly);
        Assert.Equal(1, _microphoneChanges);
        Assert.Equal(0, _loudspeakerChanges);
    }

    [WindowsFact]
    public void Changes_DeviceChangesJustAsTheQuietPeriodRunsOut_AreToldOnlyAfterItsOwn()
    {
        // Arrange
        using WindowsAudioDeviceEvents events = Events();
        IMMNotificationClient engine = events;
        engine.OnDeviceAdded(Microphone);

        // Act: the loudspeakers change in the moment between the end of the first quiet period and its telling
        engine.OnDeviceAdded(Loudspeakers);
        _clock.RunLate(timer: 0);
        (int Microphones, int Loudspeakers) toldTooEarly = (_microphoneChanges, _loudspeakerChanges);

        _clock.Advance(WindowsAudioDeviceEvents.SettleTime);

        // Assert
        Assert.Equal((0, 0), toldTooEarly);
        Assert.Equal(1, _microphoneChanges);
        Assert.Equal(1, _loudspeakerChanges);
    }

    [WindowsFact]
    public async Task Changes_EchoStoppedWhileTheyAreLookedAt_AreNotTold()
    {
        // Arrange
        using var looking = new ManualResetEventSlim();
        using var lookedAt = new ManualResetEventSlim();

        WindowsAudioDeviceEvents events = Events(_ =>
        {
            looking.Set();
            lookedAt.Wait(_patience);

            return DataFlow.Capture;
        });

        ((IMMNotificationClient)events).OnDeviceAdded(Microphone);

        Task telling = Task.Run(() => _clock.Advance(WindowsAudioDeviceEvents.SettleTime));
        Assert.True(looking.Wait(_patience));

        // Act
        events.Dispose();
        lookedAt.Set();
        await telling.WaitAsync(_patience);

        // Assert
        Assert.Equal(0, _microphoneChanges);
    }

    [WindowsFact]
    public void Changes_RealAudioEngine_AsksItWhatTheDeviceIs()
    {
        // Arrange: the engine of this computer, which has never heard of this device
        using var events = new WindowsAudioDeviceEvents(Mock.Of<ILogger<WindowsAudioDeviceEvents>>());
        using var microphones = new SemaphoreSlim(0);
        using var loudspeakers = new SemaphoreSlim(0);
        events.MicrophonesChanged += (_, _) => microphones.Release();
        events.LoudspeakersChanged += (_, _) => loudspeakers.Release();

        // Act
        ((IMMNotificationClient)events).OnDeviceRemoved("{0.0.1.00000000}.{00000000-0000-0000-0000-000000000000}");

        // Assert
        Assert.True(microphones.Wait(_patience));
        Assert.True(loudspeakers.Wait(_patience));
    }

    /// <summary>
    ///     The notices on the test's clock, counted as they are told, with the two devices of the user known unless the
    ///     test says otherwise.
    /// </summary>
    private WindowsAudioDeviceEvents Events(Func<string, DataFlow?>? flowOf = null)
    {
        var events = new WindowsAudioDeviceEvents(Mock.Of<ILogger<WindowsAudioDeviceEvents>>(), _clock, flowOf ?? FlowOf);
        events.MicrophonesChanged += (_, _) => Interlocked.Increment(ref _microphoneChanges);
        events.LoudspeakersChanged += (_, _) => Interlocked.Increment(ref _loudspeakerChanges);

        return events;
    }

    private static DataFlow? FlowOf(string deviceId) => deviceId switch
    {
        Microphone => DataFlow.Capture,
        Loudspeakers => DataFlow.Render,
        _ => null
    };

    /// <summary>
    ///     A clock that moves only when the test moves it, and runs the timers that are due on the thread that moves it.
    /// </summary>
    private sealed class ManualClock : TimeProvider
    {
        private readonly object _lock = new();
        private readonly List<ManualTimer> _timers = [];

        private TimeSpan _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, () => callback(state));
            timer.Change(dueTime, period);

            lock (_lock)
                _timers.Add(timer);

            return timer;
        }

        public void Advance(TimeSpan time)
        {
            ManualTimer[] due;

            lock (_lock)
            {
                _now += time;
                due = [.. _timers.Where(timer => timer.TakeIfDue(_now))];
            }

            // Outside the lock: what a timer runs may set the next one
            foreach (ManualTimer timer in due)
                timer.Tick();
        }

        /// <summary>
        ///     Runs a timer, in the order they were made, even though it was set again or let go since: a timer of the
        ///     system whose time ran out just before does that.
        /// </summary>
        public void RunLate(int timer)
        {
            ManualTimer late;

            lock (_lock)
                late = _timers[timer];

            late.Tick();
        }

        /// <summary>
        ///     A timer that runs once when it is due, as the code under test sets it.
        /// </summary>
        private sealed class ManualTimer(ManualClock clock, Action tick) : ITimer
        {
            private TimeSpan? _due;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._lock)
                    _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;

                return true;
            }

            /// <summary>
            ///     Whether the timer is due, taking it if it is. Called under the lock of the clock.
            /// </summary>
            public bool TakeIfDue(TimeSpan now)
            {
                if (_due is not { } due || due > now)
                    return false;

                _due = null;

                return true;
            }

            public void Tick() =>
                tick();

            public void Dispose()
            {
                lock (clock._lock)
                    _due = null;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();

                return ValueTask.CompletedTask;
            }
        }
    }
}