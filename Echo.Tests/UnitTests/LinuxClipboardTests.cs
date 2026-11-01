namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class LinuxClipboardTests
{
    [Fact]
    public void Writers_Wayland_TriesTheX11ToolsBeforeWlCopy()
    {
        // Act: on GNOME wl-copy opens a window for a moment, and the paste right after could go into it
        IEnumerable<string> programs = LinuxClipboard.Writers(DisplayServer.Wayland, hasX11: true).Select(writer => writer.Program);

        // Assert
        Assert.Equal(["xsel", "xclip", "wl-copy"], programs);
    }

    [Fact]
    public void Writers_WaylandWithoutX11Layer_HasOnlyWlCopy()
    {
        // Act & Assert
        Assert.Equal(["wl-copy"], LinuxClipboard.Writers(DisplayServer.Wayland, hasX11: false).Select(writer => writer.Program));
    }

    [Fact]
    public void Writers_X11_FillTheClipboardAndTheSelection()
    {
        // Act
        (string Program, string Clipboard, string Selection) xsel = LinuxClipboard.Writers(DisplayServer.X11, hasX11: true)[0];

        // Assert
        Assert.Equal(("xsel", "--clipboard --input", "--primary --input"), xsel);
    }

    [LinuxFact]
    public async Task SetTextAsync_NoClipboardProgram_SaysWhichOneIsMissing()
    {
        // Arrange: a search path with nothing on it
        var clipboard = new LinuxClipboard(DisplayServer.X11, hasX11: true, new LinuxPrograms(_ => "/nonexistent"));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<IOException>(() => clipboard.SetTextAsync("hello"));

        Assert.Contains("xsel / xclip", exception.Message);
    }
}