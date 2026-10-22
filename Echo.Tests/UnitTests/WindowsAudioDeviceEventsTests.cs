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
    ///     The notices on the test's clock, counted as they are told, with the two devices of the user known.
    /// </summary>
    private WindowsAudioDeviceEvents Events()
    {
        var events = new WindowsAudioDeviceEvents(Mock.Of<ILogger<WindowsAudioDeviceEvents>>(), _clock, FlowOf);
        events.MicrophonesChanged += (_, _) => _microphoneChanges++;
        events.LoudspeakersChanged += (_, _) => _loudspeakerChanges++;

        return events;
    }

    private static DataFlow? FlowOf(string deviceId) => deviceId switch
    {
        Microphone => DataFlow.Capture,
        Loudspeakers => DataFlow.Render,
        _ => null
    };

    /// <summary>
    ///     A clock that moves only when the test moves it, and runs the timers on the test's thread.
    /// </summary>
    private sealed class ManualClock : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];

        private TimeSpan _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, () => callback(state));
            timer.Change(dueTime, period);
            _timers.Add(timer);

            return timer;
        }

        public void Advance(TimeSpan time)
        {
            _now += time;

            foreach (ManualTimer timer in _timers.ToArray())
                timer.RunIfDue();
        }

        /// <summary>
        ///     A timer that runs once when it is due, as the code under test sets it.
        /// </summary>
        private sealed class ManualTimer(ManualClock clock, Action tick) : ITimer
        {
            private TimeSpan? _due;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;

                return true;
            }

            public void RunIfDue()
            {
                if (_due is not { } due || due > clock._now)
                    return;

                _due = null;
                tick();
            }

            public void Dispose() =>
                _due = null;

            public ValueTask DisposeAsync()
            {
                Dispose();

                return ValueTask.CompletedTask;
            }
        }
    }
}