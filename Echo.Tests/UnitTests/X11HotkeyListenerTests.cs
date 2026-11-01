namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class X11HotkeyListenerTests
{
    /// <summary>
    ///     The key code of the push-to-talk key, and of another key and of the modifier of the combination.
    /// </summary>
    private const int Key = 49;
    private const int OtherKey = 38;
    private const int Modifier = 37;

    [Fact]
    public void Change_AnotherKeyPressedAndLetGoWhileTheKeyIsHeld_DoesNotEndThePhrase()
    {
        // Act
        string[] changes = Changes((X11Native.KeyPress, Key), (X11Native.KeyPress, OtherKey), (X11Native.KeyRelease, OtherKey), (X11Native.KeyRelease, Key));

        // Assert
        Assert.Equal(["down", "up"], changes);
    }

    [Fact]
    public void Change_ModifierLetGoBeforeTheKey_TheKeyEndsThePhrase()
    {
        // Act: Ctrl + ` held, Ctrl let go first
        string[] changes = Changes((X11Native.KeyPress, Key), (X11Native.KeyRelease, Modifier), (X11Native.KeyRelease, Key));

        // Assert
        Assert.Equal(["down", "up"], changes);
    }

    [Fact]
    public void Change_RepeatsOfTheHeldKey_AreLeftOut()
    {
        // Act: X11 repeats only the "down" of a held key
        string[] changes = Changes((X11Native.KeyPress, Key), (X11Native.KeyPress, Key), (X11Native.KeyPress, Key), (X11Native.KeyRelease, Key));

        // Assert
        Assert.Equal(["down", "up"], changes);
    }

    /// <summary>
    ///     Runs events through <see cref="X11HotkeyListener.Change"/> the way the listener's loop does.
    /// </summary>
    private static string[] Changes(params (int Type, int Keycode)[] events)
    {
        var changes = new List<string>();
        bool isDown = false;

        foreach ((int type, int keycode) in events)
        {
            if (X11HotkeyListener.Change(type, keycode, Key, isDown) is bool down)
            {
                isDown = down;
                changes.Add(down ? "down" : "up");
            }
        }

        return [.. changes];
    }
}