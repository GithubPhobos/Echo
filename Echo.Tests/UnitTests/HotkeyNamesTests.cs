using Echo.Setup.Hotkeys;
using SharpHook.Data;

namespace Echo.Tests.UnitTests;

public sealed class HotkeyNamesTests
{
    [Fact]
    public void ToChoice_KeyWithHeldModifier_KeepsTheSideOfTheModifier()
    {
        // Act
        HotkeyChoice choice = HotkeyNames.ToChoice(KeyCode.VcF8, EventMask.RightCtrl);

        // Assert
        Assert.Equal(new HotkeyChoice("VcF8", "rightctrl"), choice);
        Assert.Equal("Right Ctrl + F8", HotkeyNames.Display(choice));
        Assert.Equal("RCtrl + F8", HotkeyNames.Display(choice, isShort: true));
    }

    [Fact]
    public void ToChoice_ModifierPressedAlone_IsTheKeyItself()
    {
        // Act
        HotkeyChoice choice = HotkeyNames.ToChoice(KeyCode.VcRightAlt, EventMask.RightAlt);

        // Assert
        Assert.Equal(new HotkeyChoice("VcRightAlt", string.Empty), choice);
        Assert.Equal("Right Alt", HotkeyNames.Display(choice));
        Assert.Equal("RAlt", HotkeyNames.Display(choice, isShort: true));
    }

    [Fact]
    public void CapturedKey_IsUnderstoodByThePushToTalkService()
    {
        // Arrange
        HotkeyChoice choice = HotkeyNames.ToChoice(KeyCode.VcBackQuote, EventMask.LeftShift);

        // Act & Assert
        Assert.Equal(KeyCode.VcBackQuote, KeyMapperService.ParseMainKey(choice.Key));
        Assert.Equal(EventMask.LeftShift, KeyMapperService.ParseModifierKeyOrThrow(choice.Modifier));
        Assert.Equal("Left Shift + `", HotkeyNames.Display(choice));
    }
}