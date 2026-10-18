using System.Runtime.Versioning;
using Echo.Platform.Linux;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class DesktopEntryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Ensure_NoLauncherYet_WritesItOnceUnderEchosName()
    {
        // Arrange
        var entry = new DesktopEntry(_directory, "/home/user/echooo/echooo");

        // Act
        bool isWritten = entry.Ensure();
        bool isWrittenAgain = entry.Ensure();

        // Assert: the desktop looks for the launcher by the application ID
        Assert.True(isWritten);
        Assert.False(isWrittenAgain);
        Assert.Equal(Path.Combine(_directory, DesktopPortal.ApplicationId + ".desktop"), entry.FilePath);
        Assert.Contains("Exec=\"/home/user/echooo/echooo\"", File.ReadAllText(entry.FilePath));
    }

    [Fact]
    public void Ensure_LauncherOfAnotherCopy_IsWrittenAnew()
    {
        // Arrange: a launcher left by Echo that was elsewhere before
        new DesktopEntry(_directory, "/old/place/echooo").Ensure();

        var entry = new DesktopEntry(_directory, "/new/place/echooo");

        // Act & Assert
        Assert.True(entry.Ensure());
        Assert.Contains("/new/place/echooo", File.ReadAllText(entry.FilePath));
    }

    [Fact]
    public void Content_IsALauncherOfATerminalProgramNamedEcho()
    {
        // Act
        string content = DesktopEntry.Content("/opt/echooo/echooo");

        // Assert
        Assert.StartsWith("[Desktop Entry]", content);
        Assert.Contains("Type=Application", content);
        Assert.Contains("Name=Echo", content);
        Assert.Contains("Terminal=true", content);
    }

    [Theory]
    [InlineData("/home/user/echooo/echooo", "\"/home/user/echooo/echooo\"")]
    [InlineData("/home/user/my apps/echooo", "\"/home/user/my apps/echooo\"")]
    [InlineData("/home/user/$weird\"`/echooo", "\"/home/user/\\$weird\\\"\\`/echooo\"")]
    public void Quote_PathOfTheExecLine_IsQuotedWithItsSpecialCharactersEscaped(string path, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, DesktopEntry.Quote(path));
    }

    [Fact]
    public void ForCurrentUser_DataFolderMoved_KeepsTheLauncherWithTheOtherLaunchers()
    {
        // Act
        var entry = DesktopEntry.ForCurrentUser(name => name == "XDG_DATA_HOME" ? "/data" : null, "/home/user");

        // Assert
        Assert.Equal(Path.Combine("/data", "applications", DesktopPortal.ApplicationId + ".desktop"), entry.FilePath);
    }
}