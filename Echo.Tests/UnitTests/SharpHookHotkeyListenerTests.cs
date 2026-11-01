using SharpHook;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class SharpHookHotkeyListenerTests
{
    [WindowsFact]
    public void Bind_InvalidMainKey_ThrowsException()
    {
        // Arrange
        using var listener = new SharpHookHotkeyListener(Mock.Of<ILogger<SharpHookHotkeyListener>>());

        // Act & Assert
        var exception = Assert.Throws<Exception>(() => listener.Bind(new HotkeyChoice("INVALID_KEY_123", string.Empty)));

        Assert.Contains("Couldn't parse push to talk key", exception.Message);
    }

    [WindowsFact]
    public void Bind_ValidKey_DoesNotThrow()
    {
        // Arrange
        using var listener = new SharpHookHotkeyListener(Mock.Of<ILogger<SharpHookHotkeyListener>>());

        // Act & Assert
        Assert.Null(Record.Exception(() => listener.Bind(new HotkeyChoice("`", "alt"))));
    }

    [WindowsFact]
    public void OnKeyPressed_KeyPressed_IsKeptFromOtherProgramsUntilReleased()
    {
        // Arrange
        using SharpHookHotkeyListener listener = CreateListener(modifier: string.Empty);

        KeyboardHookEventArgs pressed = KeyEvent(EventType.KeyPressed, KeyCode.VcF8);
        KeyboardHookEventArgs repeated = KeyEvent(EventType.KeyPressed, KeyCode.VcF8);
        KeyboardHookEventArgs released = KeyEvent(EventType.KeyReleased, KeyCode.VcF8);

        int presses = 0;
        listener.Pressed += (_, _) => presses++;

        // Act
        listener.OnKeyPressed(null, pressed);
        listener.OnKeyPressed(null, repeated);
        listener.OnKeyReleased(null, released);

        // Assert: the repeat of a held key is not another press, and goes where the first one went
        Assert.Equal(1, presses);
        Assert.True(pressed.SuppressEvent);
        Assert.True(repeated.SuppressEvent);
        Assert.True(released.SuppressEvent);
    }

    [WindowsFact]
    public void OnKeyReleased_KeyLetThrough_ThePressAndTheReleaseBothReachOtherPrograms()
    {
        // Arrange
        using SharpHookHotkeyListener listener = CreateListener(modifier: string.Empty);

        listener.Pressed += (_, e) => e.IsTaken = false;
        listener.Released += (_, e) => e.IsTaken = false;

        KeyboardHookEventArgs pressed = KeyEvent(EventType.KeyPressed, KeyCode.VcF8);
        KeyboardHookEventArgs released = KeyEvent(EventType.KeyReleased, KeyCode.VcF8);

        // Act
        listener.OnKeyPressed(null, pressed);
        listener.OnKeyReleased(null, released);

        // Assert
        Assert.False(pressed.SuppressEvent);
        Assert.False(released.SuppressEvent);
    }

    [WindowsFact]
    public void OnKeyReleased_KeyPressedWithoutItsModifier_IsNotReportedAndReachesOtherPrograms()
    {
        // Arrange
        using SharpHookHotkeyListener listener = CreateListener(modifier: "leftctrl");

        int events = 0;
        listener.Pressed += (_, _) => events++;
        listener.Released += (_, _) => events++;

        KeyboardHookEventArgs pressed = KeyEvent(EventType.KeyPressed, KeyCode.VcF8);
        KeyboardHookEventArgs released = KeyEvent(EventType.KeyReleased, KeyCode.VcF8);

        // Act
        listener.OnKeyPressed(null, pressed);
        listener.OnKeyReleased(null, released);

        // Assert
        Assert.Equal(0, events);
        Assert.False(pressed.SuppressEvent);
        Assert.False(released.SuppressEvent);
    }

    private static SharpHookHotkeyListener CreateListener(string modifier)
    {
        var listener = new SharpHookHotkeyListener(Mock.Of<ILogger<SharpHookHotkeyListener>>());

        listener.Bind(new HotkeyChoice("VcF8", modifier));

        return listener;
    }

    private static KeyboardHookEventArgs KeyEvent(EventType type, KeyCode key) =>
        new(new UioHookEvent { Type = type, Keyboard = new KeyboardEventData { KeyCode = key } });
}