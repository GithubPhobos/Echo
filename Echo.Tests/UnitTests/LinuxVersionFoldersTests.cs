using System.Diagnostics;
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
    public async Task TryRemove_FolderAProgramRunsFrom_StaysWhole()
    {
        // Arrange: a copied shell reports that it has started, then waits for input in the place of Echo.
        string version = Path.Combine(_root, "versions", "2.0.1");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "appsettings.json"), "{}");

        string program = Path.Combine(version, "echooo");
        File.Copy("/bin/sh", program);
        File.SetUnixFileMode(program, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("printf 'ready\\n'; read -r line");
        using Process running = Process.Start(start)!;

        try
        {
            Assert.Equal("ready", await running.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));

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
    public void ApplyAttributes_ArchiveMadeOnLinux_GivesThePermissionToRun()
    {
        // Arrange: the zip program keeps "rwxr-xr-x" in the upper half of the attributes
        string file = Path.Combine(Version("2.0.2"), "echooo");
        ZipArchiveEntry entry = Entry(0b111_101_101 << 16);

        // Act
        _folders.ApplyAttributes(entry, file);

        // Assert
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
            File.GetUnixFileMode(file));
    }

    [LinuxFact]
    public void ApplyAttributes_ArchiveMadeOnWindows_LeavesThePermissions()
    {
        // Arrange: Windows keeps only such marks as "archive" in the lower half
        string file = Path.Combine(Version("2.0.2"), "echooo");
        UnixFileMode before = File.GetUnixFileMode(file);

        // Act
        _folders.ApplyAttributes(Entry(0x20), file);

        // Assert
        Assert.Equal(before, File.GetUnixFileMode(file));
    }

    /// <summary>
    ///     An entry of a zip archive with these attributes.
    /// </summary>
    private static ZipArchiveEntry Entry(int attributes)
    {
        var archive = new ZipArchive(new MemoryStream(), ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("echooo");
        entry.ExternalAttributes = attributes;

        return entry;
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