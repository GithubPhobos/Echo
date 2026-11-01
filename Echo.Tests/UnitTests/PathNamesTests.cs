namespace Echo.Tests.UnitTests;

public sealed class PathNamesTests
{
    [Fact]
    public void AreSame_NamesDifferingOnlyInCapitalLetters_FollowTheSystem()
    {
        // Arrange
        string folder = Path.GetTempPath();

        // Act
        bool isSame = PathNames.AreSame(Path.Combine(folder, "Model.bin"), Path.Combine(folder, "model.bin"));

        // Assert: one file on Windows, two different files on Linux
        Assert.Equal(OperatingSystem.IsWindows(), isSame);
    }

    [Fact]
    public void IsInside_FolderThatOnlyStartsWithTheSameLetters_IsNotInside()
    {
        // Arrange
        string root = Path.Combine(Path.GetTempPath(), "home");

        // Act & Assert
        Assert.True(PathNames.IsInside(Path.Combine(root, "user"), root));
        Assert.True(PathNames.IsInside(root, root));
        Assert.False(PathNames.IsInside(Path.Combine(Path.GetTempPath(), "homework"), root));
    }
}