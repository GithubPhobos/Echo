using System.Runtime.Versioning;
using Echo.Platform.Linux;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class LinuxProgramsTests
{
    [Fact]
    public void Exists_ProgramOnTheSearchPathOrNot_IsToldApart()
    {
        if (!OperatingSystem.IsLinux())
            return;

        // Arrange
        var programs = new LinuxPrograms(Environment.GetEnvironmentVariable);

        // Act & Assert
        Assert.True(programs.Exists("sh"));
        Assert.False(programs.Exists("no-such-program-anywhere"));
    }

    [Fact]
    public async Task FeedAsync_Text_ReachesTheProgramWhole()
    {
        if (!OperatingSystem.IsLinux())
            return;

        // Arrange: tee writes what it gets into the file it is given
        string file = Path.Combine(Path.GetTempPath(), $"echooo-{Guid.NewGuid():N}.txt");
        var programs = new LinuxPrograms(Environment.GetEnvironmentVariable);

        try
        {
            // Act
            await programs.FeedAsync("tee", file, "Привет, мир ✓");

            // Assert
            Assert.Equal("Привет, мир ✓", await File.ReadAllTextAsync(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task FeedAsync_ProgramFails_Throws()
    {
        if (!OperatingSystem.IsLinux())
            return;

        // Arrange
        var programs = new LinuxPrograms(Environment.GetEnvironmentVariable);

        // Act & Assert
        await Assert.ThrowsAsync<IOException>(() => programs.FeedAsync("false", string.Empty, "anything"));
    }
}