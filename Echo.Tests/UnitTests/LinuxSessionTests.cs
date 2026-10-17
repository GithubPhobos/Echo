using Echo.Platform.Linux;

namespace Echo.Tests.UnitTests;

public sealed class LinuxSessionTests
{
    [Theory]
    [InlineData("wayland", "wayland-0", ":0", nameof(DisplayServer.Wayland))]
    [InlineData("x11", null, ":0", nameof(DisplayServer.X11))]
    // The session type wins over the variables a Wayland session sets for its X11 programs too
    [InlineData("x11", "wayland-0", ":0", nameof(DisplayServer.X11))]
    // Without a session type, as in WSL: a Wayland display wins
    [InlineData(null, "wayland-0", ":0", nameof(DisplayServer.Wayland))]
    [InlineData("tty", null, ":0", nameof(DisplayServer.X11))]
    [InlineData("tty", null, null, nameof(DisplayServer.None))]
    [InlineData(null, null, null, nameof(DisplayServer.None))]
    public void Detect_TellsTheDisplayServerFromTheSessionVariables(string? type, string? wayland, string? display, string expected)
    {
        // Arrange
        var variables = new Dictionary<string, string?>
        {
            ["XDG_SESSION_TYPE"] = type,
            ["WAYLAND_DISPLAY"] = wayland,
            ["DISPLAY"] = display,
        };

        // Act & Assert
        Assert.Equal(expected, LinuxSession.Detect(name => variables.GetValueOrDefault(name)).ToString());
    }
}