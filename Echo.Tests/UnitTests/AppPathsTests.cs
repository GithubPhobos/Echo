namespace Echo.Tests.UnitTests;

public sealed class AppPathsTests
{
    private const string UserHome = "/home/user";

    [Fact]
    public void ForLinux_NothingSet_UsesTheStandardFolders()
    {
        // Act
        AppPaths paths = AppPaths.ForLinux(_ => null, UserHome);

        // Assert
        Assert.Equal(Path.Combine(UserHome, ".config", "echooo", "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(UserHome, ".local", "share", "echooo", "models"), paths.ModelsDirectory);
        Assert.Equal(Path.Combine(UserHome, ".local", "share", "echooo", "memory"), paths.MemoryDirectory);
        Assert.Equal(Path.Combine(UserHome, ".local", "share", "echooo", "runtimes", "cuda"), paths.CudaDirectory);
        Assert.Equal(Path.Combine(UserHome, ".local", "state", "echooo", "logs"), paths.LogsDirectory);
    }

    [Fact]
    public void ForLinux_VariablesSet_FollowsThem()
    {
        // Arrange
        var variables = new Dictionary<string, string>
        {
            ["XDG_CONFIG_HOME"] = "/cfg",
            ["XDG_DATA_HOME"] = "/data",
            ["XDG_STATE_HOME"] = "/state",
        };

        // Act
        AppPaths paths = AppPaths.ForLinux(name => variables.GetValueOrDefault(name), UserHome);

        // Assert
        Assert.Equal(Path.Combine("/cfg", "echooo", "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine("/data", "echooo", "models"), paths.ModelsDirectory);
        Assert.Equal(Path.Combine("/data", "echooo", "memory"), paths.MemoryDirectory);
        Assert.Equal(Path.Combine("/state", "echooo", "logs"), paths.LogsDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/state")]
    public void ForLinux_EmptyOrRelativeVariable_IsIgnored(string value)
    {
        // Act: the standard says such a value is invalid and the default applies
        AppPaths paths = AppPaths.ForLinux(name => name == "XDG_STATE_HOME" ? value : null, UserHome);

        // Assert
        Assert.Equal(Path.Combine(UserHome, ".local", "state", "echooo", "logs"), paths.LogsDirectory);
    }

    [Fact]
    public void Constructor_NoStateFolder_KeepsTheLogNextToTheSettings()
    {
        // Act: Windows and ECHOOO_HOME keep everything together
        var paths = new AppPaths("/one", "/two");

        // Assert
        Assert.Equal(Path.Combine("/one", "logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine("/two", "memory"), paths.MemoryDirectory);
    }
}