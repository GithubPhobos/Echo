namespace Echo.Tests.UnitTests;

public sealed class DesktopKeyNamesTests
{
    [Theory]
    [InlineData("Нажмите grave", "`")]
    [InlineData("Press grave", "`")]
    [InlineData("Press CTRL+grave", "Ctrl + `")]
    [InlineData("<Control><Alt>F8", "<Control><Alt>F8")]
    [InlineData("LOGO+a", "Super + A")]
    [InlineData("Ctrl+`", "Ctrl + `")]
    [InlineData("F8", "F8")]
    public void Readable_DesktopsOwnWordsAroundTheLinuxName_IsTheKeyAsEchoNamesIt(string description, string expected)
    {
        // Act & Assert: GNOME writes its own word and the Linux name of the key, such as "grave" for the key left of 1
        Assert.Equal(expected, DesktopKeyNames.Readable(description));
    }

    [Theory]
    [InlineData("Нажмите что-нибудь")]
    [InlineData("Shortcut not set")]
    [InlineData("")]
    public void Readable_NoKeyAtTheEnd_LeavesTheDescriptionAsItIs(string description)
    {
        // Act & Assert
        Assert.Equal(description, DesktopKeyNames.Readable(description));
    }
}