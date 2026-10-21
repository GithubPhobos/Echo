using System.Runtime.Versioning;
using Echo.Platform.Linux;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class LinuxProgramsTests
{
    [LinuxFact]
    public void Exists_ProgramOnTheSearchPathOrNot_IsToldApart()
    {
        // Arrange
        var programs = new LinuxPrograms(Environment.GetEnvironmentVariable);

        // Act & Assert
        Assert.True(programs.Exists("sh"));
        Assert.False(programs.Exists("no-such-program-anywhere"));
    }

    [LinuxFact]
    public async Task FeedAsync_Text_ReachesTheProgramWhole()
    {
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

    [LinuxFact]
    public async Task FeedAsync_ProgramFails_Throws()
    {
        // Arrange
        var programs = new LinuxPrograms(Environment.GetEnvironmentVariable);

        // Act & Assert
        await Assert.ThrowsAsync<IOException>(() => programs.FeedAsync("false", string.Empty, "anything"));
    }

    [LinuxFact]
    public async Task FeedAsync_ProgramStopsReading_IsEndedWithinTheTimeLimit()
    {
        // Arrange: a program that never reads, and more text than the pipe to it holds
        var programs = new LinuxPrograms(Environment.GetEnvironmentVariable, TimeSpan.FromMilliseconds(300));

        // Act: the outer limit only keeps a broken time limit from hanging the tests
        Task feeding = programs.FeedAsync("sleep", "30", new string('x', 1024 * 1024));

        // Assert
        await Assert.ThrowsAsync<IOException>(() => feeding.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [LinuxFact]
    public async Task FeedAsync_ProgramEndsWithoutReading_Throws()
    {
        // Arrange
        var programs = new LinuxPrograms(Environment.GetEnvironmentVariable, TimeSpan.FromSeconds(5));

        // Act & Assert
        await Assert.ThrowsAsync<IOException>(() => programs.FeedAsync("true", string.Empty, new string('x', 1024 * 1024)).WaitAsync(TimeSpan.FromSeconds(10)));
    }
}