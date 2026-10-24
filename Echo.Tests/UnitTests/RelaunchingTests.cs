using Echo.Updates;

namespace Echo.Tests.UnitTests;

public sealed class RelaunchingTests
{
    [Fact]
    public void Arguments_FirstRestart_SkipsTheAnimationAndNamesTheVersionItCameFrom()
    {
        // Act
        IReadOnlyList<string> arguments = Relaunching.Arguments(["listen"], "2.0.1");

        // Assert
        Assert.Equal(["listen", "--no-banner", "--updated-from", "2.0.1"], arguments);
    }

    [Theory]
    [InlineData("--no-banner --updated-from 2.0.1")]
    [InlineData("--updated-from 2.0.1 --no-banner")]
    [InlineData("--updated-from=2.0.1 --no-banner")]
    public void Arguments_RestartAfterARestart_KeepsEveryOptionOnce(string previous)
    {
        // Act
        IReadOnlyList<string> arguments = Relaunching.Arguments(previous.Split(' '), "2.0.2");

        // Assert
        Assert.Equal(["--no-banner", "--updated-from", "2.0.2"], arguments);
    }

    [Fact]
    public void HasLauncher_FollowsTheVariable()
    {
        // Act & Assert
        Assert.True(Relaunching.HasLauncher(name => name == Relaunching.LauncherVariable ? "1" : null));
        Assert.False(Relaunching.HasLauncher(_ => null));
        Assert.False(Relaunching.HasLauncher(_ => string.Empty));
    }
}