using Echo.Listening;
using SharpHook;
using SharpHook.Data;

namespace Echo.Tests.UnitTests;

public sealed class PushToTalkMonitorServiceTests
{
    private readonly Mock<ILogger<PushToTalkMonitorService>> _loggerMock = new();
    private readonly Mock<ISoundPlayService> _soundMock = new();

    [Fact]
    public void InitializePushToTalkHotkeysOrThrow_InvalidMainKey_ThrowsException()
    {
        // Arrange
        var badSettings = new PushToTalkSettings { Key = "INVALID_KEY_123" };
        var optionsMock = Options.Create(badSettings);

        using var service = new PushToTalkMonitorService(_soundMock.Object, _loggerMock.Object, optionsMock);

        // Act & Assert
        var ex = Assert.Throws<Exception>(() => service.InitializePushToTalkHotkeysOrThrow());

        Assert.Contains("Couldn't parse push to talk key", ex.Message);
    }

    [Fact]
    public void InitializePushToTalkHotkeysOrThrow_ValidKey_DoesNotThrow()
    {
        // Arrange
        var goodSettings = new PushToTalkSettings { Key = "`", Modifier = "alt" };
        var optionsMock = Options.Create(goodSettings);

        using var service = new PushToTalkMonitorService(_soundMock.Object, _loggerMock.Object, optionsMock);

        // Act & Assert
        var exception = Record.Exception(() => service.InitializePushToTalkHotkeysOrThrow());

        Assert.Null(exception);
    }

    [Fact]
    public void OnKeyPressed_KeyPressed_IsKeptFromOtherProgramsUntilReleased()
    {
        // Arrange
        using PushToTalkMonitorService service = CreateService(modifier: null);

        KeyboardHookEventArgs pressed = KeyEvent(EventType.KeyPressed, KeyCode.VcF8);
        KeyboardHookEventArgs released = KeyEvent(EventType.KeyReleased, KeyCode.VcF8);

        // Act
        service.OnKeyPressed(null, pressed);
        service.OnKeyReleased(null, released);

        // Assert
        Assert.True(pressed.SuppressEvent);
        Assert.True(released.SuppressEvent);
    }

    [Fact]
    public void OnKeyReleased_Paused_ThePressAndTheReleaseBothReachOtherPrograms()
    {
        // Arrange
        var activity = new ListenActivity(new ScreenLog(), () => false) { IsPaused = true };
        using PushToTalkMonitorService service = CreateService(modifier: null, activity);

        KeyboardHookEventArgs pressed = KeyEvent(EventType.KeyPressed, KeyCode.VcF8);
        KeyboardHookEventArgs released = KeyEvent(EventType.KeyReleased, KeyCode.VcF8);

        // Act
        service.OnKeyPressed(null, pressed);
        service.OnKeyReleased(null, released);

        // Assert: a program never sees the key go down without coming up
        Assert.False(pressed.SuppressEvent);
        Assert.False(released.SuppressEvent);
    }

    [Fact]
    public void OnKeyReleased_KeyPressedWithoutItsModifier_ThePressAndTheReleaseBothReachOtherPrograms()
    {
        // Arrange
        using PushToTalkMonitorService service = CreateService(modifier: "leftctrl");

        KeyboardHookEventArgs pressed = KeyEvent(EventType.KeyPressed, KeyCode.VcF8);
        KeyboardHookEventArgs released = KeyEvent(EventType.KeyReleased, KeyCode.VcF8);

        // Act
        service.OnKeyPressed(null, pressed);
        service.OnKeyReleased(null, released);

        // Assert
        Assert.False(pressed.SuppressEvent);
        Assert.False(released.SuppressEvent);
    }

    private PushToTalkMonitorService CreateService(string? modifier, IListenActivity? activity = null)
    {
        var service = new PushToTalkMonitorService(
            _soundMock.Object,
            _loggerMock.Object,
            Options.Create(new PushToTalkSettings { Key = "VcF8", Modifier = modifier }),
            activity);

        service.InitializePushToTalkHotkeysOrThrow();

        return service;
    }

    private static KeyboardHookEventArgs KeyEvent(EventType type, KeyCode key) =>
        new(new UioHookEvent { Type = type, Keyboard = new KeyboardEventData { KeyCode = key } });
}