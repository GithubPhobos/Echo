using Echo.Platform.Linux;
using SharpHook.Data;

namespace Echo.Tests.UnitTests;

public sealed class XKeysTests
{
    [Theory]
    [InlineData(KeyCode.VcBackQuote, 0x60u, "grave")]
    [InlineData(KeyCode.VcA, 0x61u, "a")]
    [InlineData(KeyCode.Vc7, 0x37u, "7")]
    [InlineData(KeyCode.VcF8, 0xffc5u, "F8")]
    [InlineData(KeyCode.VcF24, 0xffd5u, "F24")]
    [InlineData(KeyCode.VcNumPad5, 0xffb5u, "KP_5")]
    [InlineData(KeyCode.VcRightControl, 0xffe4u, "Control_R")]
    public void Find_KeyOfTheSettings_HasTheKeysymOfLinux(KeyCode key, uint keysym, string name)
    {
        // Act
        (uint Keysym, string Name)? found = XKeys.Find(key);

        // Assert
        Assert.Equal((keysym, name), found);
    }

    [Theory]
    [InlineData("leftctrl", XKeys.ControlMask, "CTRL")]
    [InlineData("RightCtrl", XKeys.ControlMask, "CTRL")]
    [InlineData("alt", XKeys.Mod1Mask, "ALT")]
    [InlineData("leftshift", XKeys.ShiftMask, "SHIFT")]
    [InlineData("win", XKeys.Mod4Mask, "LOGO")]
    public void FindModifier_EitherSide_IsTheSameModifier(string setting, uint mask, string name)
    {
        // Act & Assert: X11 does not tell the left and the right Ctrl apart in a shortcut
        Assert.Equal((mask, name), XKeys.FindModifier(setting));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("hyper")]
    public void FindModifier_NoneOrUnknown_IsNull(string? setting)
    {
        // Act & Assert
        Assert.Null(XKeys.FindModifier(setting));
    }
}