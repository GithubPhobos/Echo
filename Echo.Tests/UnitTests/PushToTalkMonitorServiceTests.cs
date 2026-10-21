using Echo.Listening;
using Echo.Setup.Hotkeys;

namespace Echo.Tests.UnitTests;

public sealed class PushToTalkMonitorServiceTests
{
    /// <summary>
    ///     How long a test waits for what happens in the background.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly Mock<IHotkeyListener> _listenerMock = new();
    private readonly Mock<ILogger<PushToTalkMonitorService>> _loggerMock = new();
    private readonly Mock<ISoundPlayService> _soundMock = new();

    [Fact]
    public void InitializePushToTalkHotkeysOrThrow_BindsTheKeyAndModifierFromTheSettings()
    {
        // Arrange
        using PushToTalkMonitorService service = CreateService(modifier: "alt");

        // Act
        service.InitializePushToTalkHotkeysOrThrow();

        // Assert
        _listenerMock.Verify(listener => listener.Bind(new HotkeyChoice("VcF8", "alt")), Times.Once);
    }

    [Fact]
    public void InitializePushToTalkHotkeysOrThrow_KeyTheSystemCannotListenTo_Throws()
    {
        // Arrange
        _listenerMock.Setup(listener => listener.Bind(It.IsAny<HotkeyChoice>())).Throws(new Exception("Couldn't parse push to talk key!"));

        using PushToTalkMonitorService service = CreateService(modifier: null);

        // Act & Assert
        var exception = Assert.Throws<Exception>(service.InitializePushToTalkHotkeysOrThrow);

        Assert.Contains("Couldn't parse push to talk key", exception.Message);
    }

    [Fact]
    public async Task OnKeyPressed_PressAndRelease_StartAndEndOneRecordingAndKeepTheKey()
    {
        // Arrange
        using PushToTalkMonitorService service = CreateService(modifier: null);

        using var started = new SemaphoreSlim(0);
        using var ended = new SemaphoreSlim(0);

        service.OnRecordingTriggered += (_, _) => started.Release();
        service.OnRecordingReleased += (_, _) => ended.Release();

        var pressed = new HotkeyEventArgs();
        var released = new HotkeyEventArgs();

        // Act
        _listenerMock.Raise(listener => listener.Pressed += null, pressed);
        _listenerMock.Raise(listener => listener.Released += null, released);

        // Assert
        Assert.True(await started.WaitAsync(_patience));
        Assert.True(await ended.WaitAsync(_patience));
        Assert.True(pressed.IsTaken);
        Assert.True(released.IsTaken);
    }

    [Fact]
    public async Task OnKeyPressed_PressedAgainWhileRecording_DoesNotStartAnotherRecording()
    {
        // Arrange
        using PushToTalkMonitorService service = CreateService(modifier: null);

        int starts = 0;
        using var ended = new SemaphoreSlim(0);

        service.OnRecordingTriggered += (_, _) => Interlocked.Increment(ref starts);
        service.OnRecordingReleased += (_, _) => ended.Release();

        // Act
        _listenerMock.Raise(listener => listener.Pressed += null, new HotkeyEventArgs());
        _listenerMock.Raise(listener => listener.Pressed += null, new HotkeyEventArgs());
        _listenerMock.Raise(listener => listener.Released += null, new HotkeyEventArgs());

        // Assert: the recording event is raised in the background, and the end of the recording comes after it
        Assert.True(await ended.WaitAsync(_patience));
        Assert.Equal(1, Volatile.Read(ref starts));
    }

    [Fact]
    public void OnKeyReleased_Paused_ThePressAndTheReleaseBothReachOtherPrograms()
    {
        // Arrange
        var activity = new ListenActivity(new ScreenLog(), () => false) { IsPaused = true };
        using PushToTalkMonitorService service = CreateService(modifier: null, activity);

        var pressed = new HotkeyEventArgs();
        var released = new HotkeyEventArgs();

        // Act
        _listenerMock.Raise(listener => listener.Pressed += null, pressed);
        _listenerMock.Raise(listener => listener.Released += null, released);

        // Assert: a program never sees the key go down without coming up
        Assert.False(pressed.IsTaken);
        Assert.False(released.IsTaken);
    }

    [Fact]
    public void OnKeyPressed_Released_TheKeyReachesOtherProgramsAndRecordsNothing()
    {
        // Arrange: Echo could not start after the key had become its own
        using PushToTalkMonitorService service = CreateService(modifier: null);

        int starts = 0;
        service.OnRecordingTriggered += (_, _) => Interlocked.Increment(ref starts);

        var pressed = new HotkeyEventArgs();
        var released = new HotkeyEventArgs();

        // Act
        service.Release();

        _listenerMock.Raise(listener => listener.Pressed += null, pressed);
        _listenerMock.Raise(listener => listener.Released += null, released);

        // Assert
        Assert.False(pressed.IsTaken);
        Assert.False(released.IsTaken);
        Assert.Equal(0, Volatile.Read(ref starts));
    }

    [Fact]
    public async Task OnKeyLost_WhileRecording_DropsThePhraseInsteadOfEndingIt()
    {
        // Arrange
        using PushToTalkMonitorService service = CreateService(modifier: null);

        int ends = 0;
        using var cancelled = new SemaphoreSlim(0);
        using var started = new SemaphoreSlim(0);

        service.OnRecordingTriggered += (_, _) => started.Release();
        service.OnRecordingReleased += (_, _) => Interlocked.Increment(ref ends);
        service.OnRecordingCancelled += (_, _) => cancelled.Release();

        _listenerMock.Raise(listener => listener.Pressed += null, new HotkeyEventArgs());
        Assert.True(await started.WaitAsync(_patience));

        // Act: the desktop takes the key back while it is held
        _listenerMock.Raise(listener => listener.Lost += null, EventArgs.Empty);

        // Assert: and the next press starts a new recording
        Assert.True(await cancelled.WaitAsync(_patience));
        Assert.Equal(0, Volatile.Read(ref ends));

        _listenerMock.Raise(listener => listener.Pressed += null, new HotkeyEventArgs());
        Assert.True(await started.WaitAsync(_patience));
    }

    [Fact]
    public async Task OnKeyLost_NothingRecorded_CancelsNothing()
    {
        // Arrange
        using PushToTalkMonitorService service = CreateService(modifier: null);

        int cancels = 0;
        service.OnRecordingCancelled += (_, _) => Interlocked.Increment(ref cancels);

        // Act
        _listenerMock.Raise(listener => listener.Lost += null, EventArgs.Empty);

        // Assert: a press afterwards still starts a recording, which proves the loss was handled first
        using var started = new SemaphoreSlim(0);
        service.OnRecordingTriggered += (_, _) => started.Release();

        _listenerMock.Raise(listener => listener.Pressed += null, new HotkeyEventArgs());

        Assert.True(await started.WaitAsync(_patience));
        Assert.Equal(0, Volatile.Read(ref cancels));
    }

    private PushToTalkMonitorService CreateService(string? modifier, IListenActivity? activity = null) =>
        new(_listenerMock.Object,
            _soundMock.Object,
            _loggerMock.Object,
            Options.Create(new PushToTalkSettings { Key = "VcF8", Modifier = modifier }),
            activity);
}