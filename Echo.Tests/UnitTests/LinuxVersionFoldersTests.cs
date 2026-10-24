using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.Versioning;
using Echo.Platform.Linux;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class LinuxVersionFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly LinuxVersionFolders _folders = new();

    public LinuxVersionFoldersTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [LinuxFact]
    public void PointTo_NoLinkYet_MakesALinkRelativeToItself()
    {
        // Arrange
        string version = Version("2.0.1");
        string link = Path.Combine(_root, "current");

        // Act
        _folders.PointTo(link, version);

        // Assert: "versions/2.0.1", as the install script makes it, so the whole folder can be moved
        Assert.Equal(Path.Combine("versions", "2.0.1"), new FileInfo(link).LinkTarget);
        Assert.Equal("2.0.1", File.ReadAllText(Path.Combine(link, "echooo")));
        Assert.False(File.Exists(link + ".new"));
    }

    [LinuxFact]
    public void PointTo_LinkToAnotherVersion_LeadsToTheNewOne()
    {
        // Arrange
        string link = Path.Combine(_root, "current");
        _folders.PointTo(link, Version("2.0.1"));

        // Act
        _folders.PointTo(link, Version("2.0.2"));

        // Assert
        Assert.Equal("2.0.2", File.ReadAllText(Path.Combine(link, "echooo")));
        Assert.Equal("2.0.1", File.ReadAllText(Path.Combine(_root, "versions", "2.0.1", "echooo")));
    }

    [LinuxFact]
    public void PointTo_RealFolderInThePlaceOfTheLink_IsLeftAsItIs()
    {
        // Arrange
        string link = Path.Combine(_root, "current");
        Directory.CreateDirectory(link);
        File.WriteAllText(Path.Combine(link, "mine.txt"), "keep");

        // Act & Assert
        Assert.Throws<IOException>(() => _folders.PointTo(link, Version("2.0.1")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(link, "mine.txt")));
        Assert.False(File.Exists(link + ".new"));
    }

    [LinuxFact]
    public void TryRemove_FolderNothingRunsFrom_IsGone()
    {
        // Arrange
        string version = Version("2.0.1");

        // Act & Assert
        Assert.True(_folders.TryRemove(version));
        Assert.False(Directory.Exists(version));
    }

    [LinuxFact]
    public void TryRemove_FolderAProgramRunsFrom_StaysWhole()
    {
        // Arrange: a copy of a program of the system, waiting, in the place of Echo
        string version = Path.Combine(_root, "versions", "2.0.1");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "appsettings.json"), "{}");

        string program = Path.Combine(version, "echooo");
        File.Copy("/bin/sleep", program);
        File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        using Process running = Process.Start(program, "30");

        try
        {
            // Act
            bool isGone = _folders.TryRemove(version);

            // Assert: Linux would let the files go; Echo does not
            Assert.False(isGone);
            Assert.True(File.Exists(Path.Combine(version, "appsettings.json")));
        }
        finally
        {
            running.Kill();
            running.WaitForExit();
        }
    }

    [LinuxFact]
    public void Unpack_TarGz_KeepsThePermissionToRun()
    {
        // Arrange
        string source = Version("2.0.2");
        File.SetUnixFileMode(Path.Combine(source, "echooo"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        string archive = Path.Combine(_root, "echooo-linux-x64.tar.gz");

        using (FileStream file = File.Create(archive))
        using (var zipped = new GZipStream(file, CompressionLevel.Fastest))
            TarFile.CreateFromDirectory(source, zipped, includeBaseDirectory: false);

        string target = Path.Combine(_root, "unpacked");

        // Act
        _folders.Unpack(archive, target);

        // Assert
        Assert.Equal("2.0.2", File.ReadAllText(Path.Combine(target, "echooo")));
        Assert.True(File.GetUnixFileMode(Path.Combine(target, "echooo")).HasFlag(UnixFileMode.UserExecute));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "Assets", "sound.wav")));
    }

    /// <summary>
    ///     A version folder whose program file holds the version, so the folder it is read from can be told.
    /// </summary>
    private string Version(string version)
    {
        string directory = Path.Combine(_root, "versions", version);
        Directory.CreateDirectory(Path.Combine(directory, "Assets"));
        File.WriteAllText(Path.Combine(directory, "echooo"), version);
        File.WriteAllText(Path.Combine(directory, "Assets", "sound.wav"), "{}");

        return directory;
    }
}