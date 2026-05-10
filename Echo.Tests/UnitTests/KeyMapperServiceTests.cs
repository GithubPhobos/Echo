using SharpHook.Data;

namespace Echo.Tests.UnitTests;

public sealed class KeyMapperServiceTests
{
    [Theory]
    [InlineData("space", KeyCode.VcSpace)]
    [InlineData("`", KeyCode.VcBackQuote)]
    [InlineData("m", KeyCode.VcM)]
    [InlineData("LeftCtrl", KeyCode.VcLeftControl)]
    [InlineData("VcEscape", KeyCode.VcEscape)]
    [InlineData("Space", KeyCode.VcSpace)]
    [InlineData("-", KeyCode.VcMinus)]
    [InlineData("backspace", KeyCode.VcBackspace)]
    [InlineData("LeFtAlT", KeyCode.VcLeftAlt)]
    public void ParseMainKey_ValidKeys_ReturnsCorrectKeyCode(string input, KeyCode expected)
    {
        // Act
        KeyCode? result = KeyMapperService.ParseMainKey(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseMainKey_InvalidKey_ReturnsNull()
    {
        // Act
        KeyCode? result = KeyMapperService.ParseMainKey("DefinitelyNotAValidKey");

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("alt", EventMask.Alt)]
    [InlineData("ctrl", EventMask.Ctrl)]
    [InlineData("shift", EventMask.Shift)]
    [InlineData("leftctrl", EventMask.LeftCtrl)]
    [InlineData("rightshift", EventMask.RightShift)]
    [InlineData("win", EventMask.Meta)]
    [InlineData("leftmeta", EventMask.LeftMeta)]
    [InlineData("LeFtAlT", EventMask.LeftAlt)]
    [InlineData("WIN", EventMask.Meta)]
    [InlineData(" left ctrl ", EventMask.LeftCtrl)]
    [InlineData("right shift", EventMask.RightShift)]
    public void ParseModifierKeyOrThrow_ValidModifiers_ReturnsEventMask(string input, EventMask expected)
    {
        // Act
        EventMask? result = KeyMapperService.ParseModifierKeyOrThrow(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseModifierKeyOrThrow_InvalidModifier_ReturnsNull()
    {
        // Act
        EventMask? result = KeyMapperService.ParseModifierKeyOrThrow("SuperRandomModifier");

        // Assert
        Assert.Null(result);
    }
}