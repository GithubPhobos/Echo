using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using Echo.Platform.Linux;
using Echo.Platform.Windows;
using Echo.Setup.Downloads;
using Echo.Tests.UnitTests;
using Echo.Updates;

namespace Echo.Tests.IntegrationTests;

/// <summary>
///     The whole round of an update with the parts Echo runs with: the releases over HTTP, the folders of the
///     versions, the link "current" of this system and its permissions. Only GitHub is played, by a server on this
///     computer.
/// </summary>
public sealed class UpdateRoundTests : IDisposable
{
    private const UnixFileMode Runnable = (UnixFileMode)0b111_101_101;
    private const UnixFileMode Readable = (UnixFileMode)0b110_100_100;

    /// <summary>
    ///     What the attributes of a zip entry say about a regular file, besides its permissions.
    /// </summary>
    private const uint RegularFile = 0x8000;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly InstallLayout _layout;
    private readonly IVersionFolders _folders = SystemFolders();
    private readonly ReleaseServer _server = new();
    private readonly HttpClient _client = new();
    private readonly List<UpdateNotice> _notices = [];

    /// <summary>
    ///     A library every version has the same: large, and as large in the archive, so downloading it would show.
    /// </summary>
    private readonly byte[] _library = new byte[3_000_000];

    private TimeSpan _stallTimeout = FileDownloader.StallTimeout;

    public UpdateRoundTests()
    {
        _layout = new InstallLayout(_root);
        new Random(1).NextBytes(_library);

        // Echo 2.0.0, as the install script puts it
        string installed = _layout.VersionDirectory("2.0.0");

        WriteVersion("2.0.0", installed);
        _folders.PointTo(_layout.Current, installed);
    }

    public void Dispose()
    {
        _server.Dispose();
        _client.Dispose();

        // .NET cannot delete a junction inside a folder it deletes whole, so on Windows the link goes first
        if (OperatingSystem.IsWindows() && new DirectoryInfo(_layout.Current).LinkTarget is not null)
            Directory.Delete(_layout.Current);

        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Round_ServerGivesParts_DownloadsOnlyTheChangedFilesAndTheNextStartRemovesTheOldVersion()
    {
        // Arrange
        _server.Publish("2.0.1", Release("2.0.1"));

        // Act & Assert: Echo 2.0.0 finds 2.0.1, and /update brings it
        using (Updater running = Create("2.0.0"))
        {
            running.Start(isJustUpdated: false, Task.CompletedTask);
            await running.WhenIdleAsync();

            Assert.Equal(new UpdateStatus(UpdateStage.Available, "2.0.0", "2.0.1"), running.Status);

            running.Download();
            await running.WhenIdleAsync();

            Assert.Equal(new UpdateStatus(UpdateStage.Downloaded, "2.0.0", "2.0.1"), running.Status);
        }

        Assert.Equal([new UpdateNotice(UpdateNoticeKind.Downloaded, "2.0.1")], _notices);
        AssertVersion("2.0.1", _layout.VersionDirectory("2.0.1"));
        AssertVersion("2.0.1", _layout.Current);
        AssertVersion("2.0.0", _layout.VersionDirectory("2.0.0"));

        // The library of 3 MB stayed where it was: the end of the archive and a part for the changed files came
        Assert.InRange(_server.BytesSent, 1, _library.Length / 10);

        // The next start runs 2.0.1, which removes the folder of 2.0.0
        using (Updater next = Create("2.0.1"))
        {
            next.Start(isJustUpdated: true, Task.CompletedTask);
            await next.WhenIdleAsync();

            Assert.Equal(new UpdateStatus(UpdateStage.Latest, "2.0.1"), next.Status);
        }

        Assert.Equal(["2.0.1"], Directory.EnumerateDirectories(_layout.Versions).Select(entry => Path.GetFileName(entry)));
    }

    [LinuxFact("sh")]
    public async Task Round_OnLinux_TheNewProgramRunsThroughTheLink()
    {
        // Arrange: the program of every version is a script that tells its version
        _server.Publish("2.0.1", Release("2.0.1"));

        using (Updater running = Create("2.0.0"))
        {
            running.Start(isJustUpdated: false, Task.CompletedTask);
            await running.WhenIdleAsync();
            running.Download();
            await running.WhenIdleAsync();
        }

        // Act: started as ~/.local/bin/echooo starts it, through "current"
        using var program = Process.Start(new ProcessStartInfo(Path.Combine(_layout.Current, _folders.ExecutableName))
        {
            RedirectStandardOutput = true
        })!;

        string output = await program.StandardOutput.ReadToEndAsync();
        await program.WaitForExitAsync();

        // Assert
        Assert.Equal("2.0.1", output.Trim());
    }

    [Fact]
    public async Task Round_ServerGivesNoParts_DownloadsTheWholeArchive()
    {
        // Arrange
        byte[] archive = Archive("2.0.1");

        _server.Publish("2.0.1", new Dictionary<string, byte[]> { [_folders.ArchiveName] = archive });
        _server.IgnoresRanges = true;

        // Act
        using (Updater running = Create("2.0.0"))
        {
            running.Start(isJustUpdated: false, Task.CompletedTask);
            await running.WhenIdleAsync();
            running.Download();
            await running.WhenIdleAsync();

            Assert.Equal(UpdateStage.Downloaded, running.Status.Stage);
        }

        // Assert
        AssertVersion("2.0.1", _layout.Current);
        Assert.True(_server.BytesSent >= archive.Length);
        Assert.False(Directory.Exists(_layout.PartialDirectory("2.0.1")));
    }

    [Fact]
    public async Task Round_ArchiveDamagedOnTheWay_KeepsTheVersionInUse()
    {
        // Arrange: the table of contents gives the code another checksum than what it holds, and the whole
        // archive no longer matches the checksum of the release; the checksum of an entry lies 30 bytes before its
        // name in the table of contents, which is at the end
        byte[] archive = Archive("2.0.1");
        int name = archive.AsSpan().LastIndexOf("echooo.dll"u8);

        _server.Publish("2.0.1", new Dictionary<string, byte[]> { [_folders.ArchiveName] = archive });
        _server.Damage(_folders.ArchiveName, name - 30);

        // Act
        using (Updater running = Create("2.0.0"))
        {
            running.Start(isJustUpdated: false, Task.CompletedTask);
            await running.WhenIdleAsync();
            running.Download();
            await running.WhenIdleAsync();

            Assert.Equal(new UpdateStatus(UpdateStage.Available, "2.0.0", "2.0.1"), running.Status);
        }

        // Assert: the changed files failed their checksum, so the whole archive came, and failed its own
        UpdateNotice notice = Assert.Single(_notices);

        Assert.Equal(UpdateNoticeKind.DownloadFailed, notice.Kind);
        Assert.Contains("checksum mismatch", notice.Reason);
        AssertVersion("2.0.0", _layout.Current);
        Assert.Equal(["2.0.0"], Directory.EnumerateFileSystemEntries(_layout.Versions).Select(entry => Path.GetFileName(entry)));
    }

    [Fact]
    public async Task Round_ServerStopsSending_GivesUpAndKeepsTheVersionInUse()
    {
        // Arrange: the storage sends half of what is asked of the archive and then nothing, keeping the connection
        _server.Publish("2.0.1", Release("2.0.1"));
        _server.StallsOn = _folders.ArchiveName;
        _stallTimeout = TimeSpan.FromMilliseconds(500);

        // Act: first the changed files stall, then the whole archive
        using (Updater running = Create("2.0.0"))
        {
            running.Start(isJustUpdated: false, Task.CompletedTask);
            await running.WhenIdleAsync();
            running.Download();
            await running.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(20));

            // Assert: /update can try again
            Assert.Equal(new UpdateStatus(UpdateStage.Available, "2.0.0", "2.0.1"), running.Status);
        }

        UpdateNotice notice = Assert.Single(_notices);

        Assert.Equal(UpdateNoticeKind.DownloadFailed, notice.Kind);
        Assert.Contains("sent nothing", notice.Reason);
        AssertVersion("2.0.0", _layout.Current);
        Assert.Equal(["2.0.0"], Directory.EnumerateFileSystemEntries(_layout.Versions).Select(entry => Path.GetFileName(entry)));
    }

    [Fact]
    public async Task Round_FolderLeftByAnEarlierAttemptCannotBeRemovedWhole_KeepsTheVersionInUse()
    {
        // Arrange: 2.0.0 finds 2.0.1, and then a folder of 2.0.1 is there, left by an attempt that broke off
        _server.Publish("2.0.1", Release("2.0.1"));

        using Updater running = Create("2.0.0");

        running.Start(isJustUpdated: false, Task.CompletedTask);
        await running.WhenIdleAsync();

        string leftover = _layout.VersionDirectory("2.0.1");
        WriteVersion("2.0.1", leftover);

        // Act: another program holds a file of it, so only some of its files can be removed
        using (Hold(leftover))
        {
            running.Download();
            await running.WhenIdleAsync();
        }

        // Assert: what is left of it is never pointed at
        UpdateNotice notice = Assert.Single(_notices);

        Assert.Equal(UpdateNoticeKind.DownloadFailed, notice.Kind);
        Assert.Contains("cannot be removed", notice.Reason);
        Assert.Equal(new UpdateStatus(UpdateStage.Available, "2.0.0", "2.0.1"), running.Status);
        AssertVersion("2.0.0", _layout.Current);

        // Once the program lets go, /update makes the folder anew
        running.Download();
        await running.WhenIdleAsync();

        Assert.Equal(UpdateStage.Downloaded, running.Status.Stage);
        AssertVersion("2.0.1", _layout.Current);
    }

    [Fact]
    public async Task Round_SecondEchoStartsWhileTheFirstPutsTheVersionInPlace_LeavesItAlone()
    {
        // Arrange: the first Echo has made the folder of 2.0.1 and is about to point "current" at it
        _server.Publish("2.0.1", Release("2.0.1"));

        var gated = new GatedFolders(_folders);
        using Updater first = Create("2.0.0", gated);

        first.Start(isJustUpdated: false, Task.CompletedTask);
        await first.WhenIdleAsync();
        first.Download();
        await gated.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // Act: a second Echo of the version in use starts, which tidies the versions at its start
        using (Updater second = Create("2.0.0"))
        {
            second.Start(isJustUpdated: false, Task.CompletedTask);
            await second.WhenIdleAsync();
        }

        gated.Opened.SetResult();
        await first.WhenIdleAsync();

        // Assert
        Assert.Equal(UpdateStage.Downloaded, first.Status.Stage);
        AssertVersion("2.0.1", _layout.Current);
    }

    [Fact]
    public async Task Round_TwoEchoesUpdateAtOnce_BothSwitchToTheWholeVersion()
    {
        // Arrange
        _server.Publish("2.0.1", Release("2.0.1"));

        using Updater first = Create("2.0.0");
        using Updater second = Create("2.0.0");

        first.Start(isJustUpdated: false, Task.CompletedTask);
        second.Start(isJustUpdated: false, Task.CompletedTask);
        await Task.WhenAll(first.WhenIdleAsync(), second.WhenIdleAsync());

        // Act
        first.Download();
        second.Download();
        await Task.WhenAll(first.WhenIdleAsync(), second.WhenIdleAsync());

        // Assert: one made the folder, and the other found it in place
        Assert.Equal(UpdateStage.Downloaded, first.Status.Stage);
        Assert.Equal(UpdateStage.Downloaded, second.Status.Stage);
        AssertVersion("2.0.1", _layout.Current);
    }

    private Updater Create(string installed, IVersionFolders? folders = null)
    {
        var updater = new Updater(
            new GitHubReleaseSource(_client, _server.LatestRelease, _folders.ArchiveName),
            new FileDownloader(_client, _stallTimeout),
            new VersionArchive(_client, _folders, _stallTimeout),
            folders ?? _folders,
            new MemorySettings(),
            TimeProvider.System,
            _layout,
            installed,
            _layout.VersionDirectory(installed));

        updater.Noticed += (_, notice) =>
        {
            lock (_notices)
                _notices.Add(notice);
        };

        return updater;
    }

    /// <summary>
    ///     The files of a version, by their names in the archive, with the permissions they have on Linux. From 2.0.0
    ///     to 2.0.1 the program and the code change, the library and the sound stay, one file goes and one comes.
    /// </summary>
    private Dictionary<string, (byte[] Contents, UnixFileMode Mode)> Files(string version)
    {
        var files = new Dictionary<string, (byte[] Contents, UnixFileMode Mode)>
        {
            [_folders.ExecutableName] = (Text($"#!/bin/sh\necho {version}\n"), Runnable),
            ["echooo.dll"] = (Text($"the code of Echo {version}"), Readable),
            ["library.dll"] = (_library, Readable),
            ["Assets/start-recording.wav"] = (Text("a sound"), Readable)
        };

        if (version == "2.0.0")
            files["old.dll"] = (Text("gone in 2.0.1"), Readable);
        else
            files["de/echooo.resources.dll"] = (Text($"Echo {version} auf Deutsch"), Readable);

        return files;
    }

    /// <summary>
    ///     Writes the files of a version, as the install script or an update unpacks them.
    /// </summary>
    private void WriteVersion(string version, string directory)
    {
        foreach ((string name, (byte[] contents, UnixFileMode mode)) in Files(version))
        {
            string file = Path.Combine(directory, name);

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, contents);

            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(file, mode);
        }
    }

    /// <summary>
    ///     The release's archive of this system, as the release builds it.
    /// </summary>
    private byte[] Archive(string version)
    {
        var archive = new MemoryStream();

        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create))
        {
            foreach ((string name, (byte[] contents, UnixFileMode mode)) in Files(version))
            {
                ZipArchiveEntry entry = zip.CreateEntry(name);

                // The zip program of Linux keeps the permissions of a file in the upper half of its attributes
                if (OperatingSystem.IsLinux())
                    entry.ExternalAttributes = (int)((RegularFile | (uint)mode) << 16);

                using Stream stream = entry.Open();
                stream.Write(contents);
            }
        }

        return archive.ToArray();
    }

    private Dictionary<string, byte[]> Release(string version) =>
        new() { [_folders.ArchiveName] = Archive(version) };

    /// <summary>
    ///     Checks that <paramref name="directory"/> holds the files of the version, no more and no fewer, each as it is
    ///     in the archive, and on Linux with its permissions.
    /// </summary>
    private void AssertVersion(string version, string directory)
    {
        Dictionary<string, (byte[] Contents, UnixFileMode Mode)> expected = Files(version);

        Assert.Equal(
            expected.Keys.Order(),
            Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
                .Order());

        foreach ((string name, (byte[] contents, UnixFileMode mode)) in expected)
        {
            string file = Path.Combine(directory, name);

            Assert.Equal(contents, File.ReadAllBytes(file));

            if (!OperatingSystem.IsWindows())
                Assert.Equal(mode, File.GetUnixFileMode(file));
        }
    }

    private static byte[] Text(string text) =>
        Encoding.UTF8.GetBytes(text);

    /// <summary>
    ///     Keeps a file of the version in <paramref name="directory"/> from being deleted, as another program such as an
    ///     antivirus can: on Windows by holding the library open, on Linux by taking away the right to change the
    ///     folder of the sounds.
    /// </summary>
    private static IDisposable Hold(string directory)
    {
        if (OperatingSystem.IsWindows())
            return File.Open(Path.Combine(directory, "library.dll"), FileMode.Open, FileAccess.Read, FileShare.Read);

        return new UnchangeableFolder(Path.Combine(directory, "Assets"));
    }

    /// <summary>
    ///     The way the system the tests run on keeps versions.
    /// </summary>
    private static IVersionFolders SystemFolders()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsVersionFolders();

        if (OperatingSystem.IsLinux())
            return new LinuxVersionFolders();

        throw new PlatformNotSupportedException();
    }

    /// <summary>
    ///     A folder no one may change while it is held, so the files in it cannot be deleted.
    /// </summary>
    [UnsupportedOSPlatform("windows")]
    private sealed class UnchangeableFolder : IDisposable
    {
        private readonly string _folder;

        public UnchangeableFolder(string folder)
        {
            _folder = folder;
            File.SetUnixFileMode(folder, Runnable & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
        }

        public void Dispose() => File.SetUnixFileMode(_folder, Runnable);
    }

    /// <summary>
    ///     The folders of this system, which stop before pointing the link until the test opens them.
    /// </summary>
    private sealed class GatedFolders(IVersionFolders folders) : IVersionFolders
    {
        /// <summary>
        ///     Set when the link is about to be pointed.
        /// </summary>
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ArchiveName => folders.ArchiveName;

        public string ExecutableName => folders.ExecutableName;

        public string InstallCommand => folders.InstallCommand;

        public void ApplyAttributes(ZipArchiveEntry entry, string file) => folders.ApplyAttributes(entry, file);

        public bool TryRemove(string directory) => folders.TryRemove(directory);

        public void PointTo(string link, string directory)
        {
            Reached.TrySetResult();
            Opened.Task.Wait();

            folders.PointTo(link, directory);
        }
    }
}