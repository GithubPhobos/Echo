namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class WindowsVersionFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly WindowsVersionFolders _folders = new();

    public WindowsVersionFoldersTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // .NET cannot delete a junction inside a folder it deletes whole, so the link goes first
        string link = Path.Combine(_root, "current");

        if (new DirectoryInfo(link).LinkTarget is not null)
            Directory.Delete(link);

        Directory.Delete(_root, recursive: true);
    }

    [WindowsFact]
    public void PointTo_NoLinkYet_MakesAJunctionToTheVersion()
    {
        // Arrange
        string version = Version("2.0.1");
        string link = Path.Combine(_root, "current");

        // Act
        _folders.PointTo(link, version);

        // Assert: a junction, through which the files of the version are read
        Assert.True(new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal(version, Directory.ResolveLinkTarget(link, returnFinalTarget: true)!.FullName);
        Assert.Equal("2.0.1", File.ReadAllText(Path.Combine(link, "echooo.exe")));
    }

    [WindowsFact]
    public void PointTo_LinkToAnotherVersion_LeadsToTheNewOneAndLeavesTheOldOneWhole()
    {
        // Arrange
        string old = Version("2.0.1");
        string link = Path.Combine(_root, "current");
        _folders.PointTo(link, old);

        // Act
        _folders.PointTo(link, Version("2.0.2"));

        // Assert
        Assert.Equal("2.0.2", File.ReadAllText(Path.Combine(link, "echooo.exe")));
        Assert.Equal("2.0.1", File.ReadAllText(Path.Combine(old, "echooo.exe")));
    }

    [WindowsFact]
    public void PointTo_RealFolderInThePlaceOfTheLink_IsLeftAsItIs()
    {
        // Arrange
        string link = Path.Combine(_root, "current");
        Directory.CreateDirectory(link);
        File.WriteAllText(Path.Combine(link, "mine.txt"), "keep");

        // Act & Assert
        Assert.Throws<IOException>(() => _folders.PointTo(link, Version("2.0.1")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(link, "mine.txt")));
    }

    [WindowsFact]
    public void TryRemove_FolderNothingRunsFrom_IsGone()
    {
        // Arrange
        string version = Version("2.0.1");

        // Act & Assert
        Assert.True(_folders.TryRemove(version));
        Assert.False(Directory.Exists(version));
    }

    [WindowsFact]
    public void TryRemove_FolderAProgramRunsFrom_StaysWhole()
    {
        // Arrange: a copy of a program of the system, waiting, in the place of Echo
        string version = Path.Combine(_root, "versions", "2.0.1");
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "appsettings.json"), "{}");

        string program = Path.Combine(version, "echooo.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), program);

        using Process running = Process.Start(new ProcessStartInfo(program, "-n 30 127.0.0.1") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;

        try
        {
            // Act
            bool isGone = _folders.TryRemove(version);

            // Assert: not a file of it is touched
            Assert.False(isGone);
            Assert.True(File.Exists(Path.Combine(version, "appsettings.json")));
        }
        finally
        {
            running.Kill();
            running.WaitForExit();
        }
    }

    /// <summary>
    ///     A version folder whose program file holds the version, so the folder it is read from can be told.
    /// </summary>
    private string Version(string version)
    {
        string directory = Path.Combine(_root, "versions", version);
        Directory.CreateDirectory(Path.Combine(directory, "Assets"));
        File.WriteAllText(Path.Combine(directory, "echooo.exe"), version);
        File.WriteAllText(Path.Combine(directory, "Assets", "sound.wav"), "{}");

        return directory;
    }
}