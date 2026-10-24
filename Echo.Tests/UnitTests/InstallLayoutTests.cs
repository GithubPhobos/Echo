using Echo.Platform.Linux;
using Echo.Platform.Windows;
using Echo.Updates;

namespace Echo.Tests.UnitTests;

public sealed class InstallLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // .NET cannot delete a junction inside a folder it deletes whole, so on Windows the link goes first
        string link = Path.Combine(_root, "current");

        if (OperatingSystem.IsWindows() && new DirectoryInfo(link).LinkTarget is not null)
            Directory.Delete(link);

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Of_VersionFolderOfTheInstallScript_IsTheLayout()
    {
        // Arrange
        string version = Path.Combine(_root, "versions", "2.0.1");
        Directory.CreateDirectory(version);
        Directory.CreateDirectory(Path.Combine(_root, "current"));

        // Act
        InstallLayout? layout = InstallLayout.Of(version + Path.DirectorySeparatorChar);

        // Assert
        Assert.NotNull(layout);
        Assert.Equal(_root, layout.Root);
        Assert.Equal(Path.Combine(_root, "current"), layout.Current);
        Assert.Equal(Path.Combine(_root, "versions", "2.0.2"), layout.VersionDirectory("2.0.2"));
        Assert.Equal(Path.Combine(_root, "versions", "2.0.2.part"), layout.PartialDirectory("2.0.2"));
    }

    [Fact]
    public void Of_FolderUnpackedByHandOrBuiltByADeveloper_IsNoLayout()
    {
        // Arrange: a version folder without the link, and a build folder
        string version = Path.Combine(_root, "versions", "2.0.1");
        string build = Path.Combine(_root, "bin", "Release");
        Directory.CreateDirectory(version);
        Directory.CreateDirectory(build);

        // Act & Assert
        Assert.Null(InstallLayout.Of(version));
        Assert.Null(InstallLayout.Of(build));
        Assert.Null(InstallLayout.Of(Path.GetPathRoot(_root)!));
    }

    [Fact]
    public void CurrentDirectory_Link_IsTheFolderItLeadsTo()
    {
        // Arrange
        var layout = new InstallLayout(_root);
        IVersionFolders system = SystemFolders();

        Directory.CreateDirectory(layout.VersionDirectory("2.0.2"));
        system.PointTo(layout.Current, layout.VersionDirectory("2.0.2"));

        // Act
        string? current = layout.CurrentDirectory();

        // Assert
        Assert.Equal(layout.VersionDirectory("2.0.2"), current);
        Assert.Null(new InstallLayout(Path.Combine(_root, "elsewhere")).CurrentDirectory());
    }

    /// <summary>
    ///     The way the system the tests run on keeps versions, for a real link.
    /// </summary>
    private static IVersionFolders SystemFolders()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsVersionFolders();

        if (OperatingSystem.IsLinux())
            return new LinuxVersionFolders();

        throw new PlatformNotSupportedException();
    }
}