using System.Diagnostics;
using System.IO.Compression;
using Echo.Platform.Linux;
using Echo.Platform.Windows;
using Echo.Setup;
using Echo.Setup.Downloads;
using Echo.Updates;

namespace Echo.Tests.UnitTests;

public sealed class UpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly InstallLayout _layout;
    private readonly Mock<IReleaseSource> _source = new();
    private readonly FakeDownloader _downloader = new();
    private readonly FakeArchive _archive = new();
    private readonly FakeFolders _folders = new();
    private readonly ManualTime _time = new();
    private readonly List<UpdateNotice> _notices = [];
    private bool _isAutomatic;

    public UpdaterTests()
    {
        _layout = new InstallLayout(_root);

        Directory.CreateDirectory(_layout.VersionDirectory("2.0.1"));
        Directory.CreateDirectory(_layout.Current);

        _source.Setup(source => source.ChecksumAsync(It.IsAny<ReleaseInfo>(), It.IsAny<CancellationToken>())).ReturnsAsync(new string('a', 64));
    }

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
    public async Task Start_NewerVersionOutInManualMode_ShowsItAndWaitsForUpdate()
    {
        // Arrange
        Latest("2.0.2");

        // Act
        Updater updater = await StartAsync();

        // Assert: nothing is downloaded and nothing is said; the screen shows the arrow
        Assert.Equal(new UpdateStatus(UpdateStage.Available, "2.0.1", "2.0.2"), updater.Status);
        Assert.Equal(0, _downloader.Calls);
        Assert.Empty(_notices);
    }

    [Fact]
    public async Task Start_NewerVersionOutInAutomaticMode_DownloadsItAndPointsTheLinkAtIt()
    {
        // Arrange
        _isAutomatic = true;
        Latest("2.0.2");

        // Act
        Updater updater = await StartAsync();

        // Assert
        Assert.Equal(new UpdateStatus(UpdateStage.Downloaded, "2.0.1", "2.0.2"), updater.Status);
        Assert.Equal([UpdateNoticeKind.DownloadStarted, UpdateNoticeKind.Downloaded], _notices.Select(notice => notice.Kind));
        Assert.Equal([(_layout.Current, _layout.VersionDirectory("2.0.2"))], _folders.Links);
        Assert.True(File.Exists(Path.Combine(_layout.VersionDirectory("2.0.2"), _folders.ExecutableName)));

        // The archive was checked against the checksum of the release, and nothing of the download is left
        Assert.Equal(new string('a', 64), _downloader.LastRequest!.Sha256);
        Assert.False(Directory.Exists(_layout.PartialDirectory("2.0.2")));
    }

    [Fact]
    public async Task Download_ServerGivesParts_DownloadsOnlyTheChangedFiles()
    {
        // Arrange
        _archive.GivesParts = true;
        Latest("2.0.2");
        Updater updater = await StartAsync();

        // Act
        updater.Download();
        await updater.WhenIdleAsync();

        // Assert: the new version is made from the folder of this one, without the whole archive
        Assert.Equal(new UpdateStatus(UpdateStage.Downloaded, "2.0.1", "2.0.2"), updater.Status);
        Assert.Equal(_layout.VersionDirectory("2.0.1") + Path.DirectorySeparatorChar, _archive.SameFrom);
        Assert.Equal("changes", File.ReadAllText(Path.Combine(_layout.VersionDirectory("2.0.2"), _folders.ExecutableName)));
        Assert.Equal([(_layout.Current, _layout.VersionDirectory("2.0.2"))], _folders.Links);
        Assert.Equal(0, _downloader.Calls);
    }

    [Fact]
    public async Task Download_ServerGivesNoParts_DownloadsTheWholeArchive()
    {
        // Arrange: what was made of the parts before the server refused is not kept
        Latest("2.0.2");
        Updater updater = await StartAsync();

        // Act
        updater.Download();
        await updater.WhenIdleAsync();

        // Assert
        string target = _layout.VersionDirectory("2.0.2");

        Assert.Equal(UpdateStage.Downloaded, updater.Status.Stage);
        Assert.Equal([UpdateNoticeKind.Downloaded], _notices.Select(notice => notice.Kind));
        Assert.Equal(1, _downloader.Calls);
        Assert.Equal("archive", File.ReadAllText(Path.Combine(target, _folders.ExecutableName)));
        Assert.False(File.Exists(Path.Combine(target, "half")));
    }

    [Fact]
    public async Task Download_ChangedFilesUnderWay_ShowsHowFarItHasGot()
    {
        // Arrange
        _archive.GivesParts = true;
        _archive.Gate = new TaskCompletionSource();
        Latest("2.0.2");
        Updater updater = await StartAsync();

        // Act
        updater.Download();
        await _archive.Reported.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Equal(new UpdateStatus(UpdateStage.Downloading, "2.0.1", "2.0.2", 0.25), updater.Status);

        _archive.Gate.SetResult();
        await updater.WhenIdleAsync();
    }

    [Theory]
    [InlineData("2.0.1")]
    [InlineData("2.0.0")]
    public async Task Start_NoNewerVersion_IsTheLatest(string latest)
    {
        // Arrange: a developer's build of the released version is not older than it
        Latest(latest);

        // Act
        Updater updater = await StartAsync();

        // Assert
        Assert.Equal(new UpdateStatus(UpdateStage.Latest, "2.0.1"), updater.Status);
        Assert.Empty(_notices);
    }

    [Fact]
    public async Task Start_LatestReleaseIsOlderAndHasNoArchive_IsTheLatest()
    {
        // Arrange: the latest release is still one of Echo 1.x, whose files Echo 2 cannot install
        _source.Setup(source => source.LatestAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Release("1.0.1") with { ArchiveUrl = null, ChecksumsUrl = null });

        // Act
        Updater updater = await StartAsync();

        // Assert
        Assert.Equal(new UpdateStatus(UpdateStage.Latest, "2.0.1"), updater.Status);
        Assert.Empty(_notices);
    }

    [Fact]
    public async Task Download_NewerReleaseWithoutTheArchive_SaysWhyAndDownloadsNothing()
    {
        // Arrange
        _source.Setup(source => source.LatestAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Release("2.0.2") with { ArchiveUrl = null });
        Updater updater = await StartAsync();

        // Act
        UpdateStart start = updater.Download();
        await updater.WhenIdleAsync();

        // Assert: as if the releases had not answered
        Assert.Equal(UpdateStage.Unknown, updater.Status.Stage);
        Assert.Equal(UpdateStart.Checking, start);

        UpdateNotice notice = Assert.Single(_notices);

        Assert.Equal(UpdateNoticeKind.CheckFailed, notice.Kind);
        Assert.Contains("Echo 2.0.2 is out, but has no echooo-test.zip", notice.Reason);
        Assert.Equal(0, _downloader.Calls);
    }

    [Fact]
    public async Task Start_ReleasesCannotBeReached_SaysNothing()
    {
        // Arrange
        _source.Setup(source => source.LatestAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("no route to host"));

        // Act
        Updater updater = await StartAsync();

        // Assert
        Assert.Equal(UpdateStage.Unknown, updater.Status.Stage);
        Assert.Empty(_notices);
    }

    [Fact]
    public async Task Start_RightAfterAnUpdate_IsTheLatestBeforeTheCheckAnswers()
    {
        // Arrange
        var answer = new TaskCompletionSource<ReleaseInfo>();
        _source.Setup(source => source.LatestAsync(It.IsAny<CancellationToken>())).Returns(answer.Task);

        // Act
        Updater updater = Create();
        updater.Start(isJustUpdated: true, Task.CompletedTask);

        // Assert
        Assert.Equal(new UpdateStatus(UpdateStage.Latest, "2.0.1"), updater.Status);

        answer.SetResult(Release("2.0.1"));
        await updater.WhenIdleAsync();
    }

    [Fact]
    public async Task Start_BeforeEchoIsReady_RemovesOldVersionsButChecksOnlyOnceItIs()
    {
        // Arrange
        Latest("2.0.2");
        Directory.CreateDirectory(_layout.VersionDirectory("2.0.0"));

        var ready = new TaskCompletionSource();
        Updater updater = Create();

        // Act
        updater.Start(isJustUpdated: false, ready.Task);
        await WaitUntilAsync(() => !Directory.Exists(_layout.VersionDirectory("2.0.0")));

        // Assert: what the check tells would come among the lines of the start-up
        Assert.Equal(UpdateStage.Unknown, updater.Status.Stage);
        _source.Verify(source => source.LatestAsync(It.IsAny<CancellationToken>()), Times.Never);

        ready.SetResult();
        await updater.WhenIdleAsync();

        Assert.Equal(new UpdateStatus(UpdateStage.Available, "2.0.1", "2.0.2"), updater.Status);
    }

    [Fact]
    public async Task Start_EveryDay_ChecksAgain()
    {
        // Arrange
        Latest("2.0.1");
        Updater updater = await StartAsync();

        Latest("2.0.2");

        // Act: a day later
        Assert.Equal(Updater.CheckInterval, _time.Period);
        _time.Fire();
        await updater.WhenIdleAsync();

        // Assert
        Assert.Equal(new UpdateStatus(UpdateStage.Available, "2.0.1", "2.0.2"), updater.Status);
    }

    [Fact]
    public async Task Download_NewerVersionWaiting_DownloadsItWithoutTellingTheLogItStarted()
    {
        // Arrange: /update says itself that the download started
        Latest("2.0.2");
        Updater updater = await StartAsync();

        // Act
        UpdateStart start = updater.Download();
        await updater.WhenIdleAsync();

        // Assert
        Assert.Equal(UpdateStart.Started, start);
        Assert.Equal(UpdateStage.Downloaded, updater.Status.Stage);
        Assert.Equal([UpdateNoticeKind.Downloaded], _notices.Select(notice => notice.Kind));
    }

    [Fact]
    public async Task Download_NothingKnownYet_ChecksAndDownloadsWhatItFinds()
    {
        // Arrange: the check at the start could not reach the releases
        _source.Setup(source => source.LatestAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("no route to host"));
        Updater updater = await StartAsync();

        Latest("2.0.2");

        // Act
        UpdateStart start = updater.Download();
        await updater.WhenIdleAsync();

        // Assert
        Assert.Equal(UpdateStart.Checking, start);
        Assert.Equal(UpdateStage.Downloaded, updater.Status.Stage);
        Assert.Equal([UpdateNoticeKind.DownloadStarted, UpdateNoticeKind.Downloaded], _notices.Select(notice => notice.Kind));
    }

    [Fact]
    public async Task Download_NothingKnownYetAndStillUnreachable_SaysWhy()
    {
        // Arrange
        _source.Setup(source => source.LatestAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("no route to host"));
        Updater updater = await StartAsync();

        // Act
        updater.Download();
        await updater.WhenIdleAsync();

        // Assert
        UpdateNotice notice = Assert.Single(_notices);

        Assert.Equal(new UpdateNotice(UpdateNoticeKind.CheckFailed, "2.0.1", "no route to host"), notice);
    }

    [Fact]
    public async Task Download_NothingKnownYetAndNothingNewer_SaysItIsTheLatest()
    {
        // Arrange
        _source.Setup(source => source.LatestAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("no route to host"));
        Updater updater = await StartAsync();

        Latest("2.0.1");

        // Act
        updater.Download();
        await updater.WhenIdleAsync();

        // Assert
        Assert.Equal([new UpdateNotice(UpdateNoticeKind.UpToDate, "2.0.1")], _notices);
    }

    [Fact]
    public async Task Download_AtEveryStage_SaysWhereItIs()
    {
        // Arrange
        Latest("2.0.1");
        Updater latest = await StartAsync();

        Latest("2.0.2");
        _downloader.Gate = new TaskCompletionSource();
        Updater downloading = await StartAsync();
        downloading.Download();

        // Act & Assert
        Assert.Equal(UpdateStart.UpToDate, latest.Download());
        Assert.Equal(UpdateStart.AlreadyDownloading, downloading.Download());

        _downloader.Gate.SetResult();
        await downloading.WhenIdleAsync();

        Assert.Equal(UpdateStart.AlreadyDownloaded, downloading.Download());
        Assert.Equal(1, _downloader.Calls);
    }

    [Fact]
    public async Task Download_WhileUnderWay_ShowsHowFarItHasGot()
    {
        // Arrange
        Latest("2.0.2");
        _downloader.Gate = new TaskCompletionSource();
        Updater updater = await StartAsync();

        // Act
        updater.Download();
        await _downloader.Reported.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.Equal(new UpdateStatus(UpdateStage.Downloading, "2.0.1", "2.0.2", 0.5), updater.Status);
        Assert.Equal(50, updater.Status.Percent);

        _downloader.Gate.SetResult();
        await updater.WhenIdleAsync();
    }

    [Fact]
    public async Task Download_NotInstalledByTheScript_DownloadsNothing()
    {
        // Arrange
        Latest("2.0.2");
        Updater updater = Create(isInstalled: false);
        updater.Start(isJustUpdated: false, Task.CompletedTask);
        await updater.WhenIdleAsync();

        // Act
        UpdateStart start = updater.Download();

        // Assert: the newer version is shown, yet only the install script can bring it
        Assert.Equal(UpdateStage.Available, updater.Status.Stage);
        Assert.Equal(UpdateStart.NotInstalledByScript, start);
        Assert.Equal(0, _downloader.Calls);
    }

    [Fact]
    public async Task Download_Fails_SaysWhyAndOffersItAgain()
    {
        // Arrange
        Latest("2.0.2");
        _downloader.Failure = new DownloadException("The downloaded file is damaged (checksum mismatch)");
        Updater updater = await StartAsync();

        // Act
        updater.Download();
        await updater.WhenIdleAsync();

        // Assert: the link is left as it was, and nothing of the download stays
        Assert.Equal(new UpdateStatus(UpdateStage.Available, "2.0.1", "2.0.2"), updater.Status);
        Assert.Equal([new UpdateNotice(UpdateNoticeKind.DownloadFailed, "2.0.2", "The downloaded file is damaged (checksum mismatch)")], _notices);
        Assert.Empty(_folders.Links);
        Assert.False(Directory.Exists(_layout.PartialDirectory("2.0.2")));

        // Another /update tries again
        _downloader.Failure = null;

        Assert.Equal(UpdateStart.Started, updater.Download());
        await updater.WhenIdleAsync();
        Assert.Equal(UpdateStage.Downloaded, updater.Status.Stage);
    }

    [Fact]
    public async Task Download_ArchiveWithoutTheProgram_IsNotPointedAt()
    {
        // Arrange
        Latest("2.0.2");
        _archive.IsEmpty = true;
        Updater updater = await StartAsync();

        // Act
        updater.Download();
        await updater.WhenIdleAsync();

        // Assert
        UpdateNotice notice = Assert.Single(_notices);

        Assert.Equal(UpdateNoticeKind.DownloadFailed, notice.Kind);
        Assert.Contains("has no echooo", notice.Reason);
        Assert.Empty(_folders.Links);
    }

    [Fact]
    public async Task Download_FolderOfTheVersionLeftByAnEarlierAttempt_IsUnpackedAnew()
    {
        // Arrange: a folder unpacked only in part
        Latest("2.0.2");
        Directory.CreateDirectory(_layout.VersionDirectory("2.0.2"));
        File.WriteAllText(Path.Combine(_layout.VersionDirectory("2.0.2"), "half"), string.Empty);

        _isAutomatic = true;

        // Act
        await StartAsync();

        // Assert
        Assert.False(File.Exists(Path.Combine(_layout.VersionDirectory("2.0.2"), "half")));
        Assert.True(File.Exists(Path.Combine(_layout.VersionDirectory("2.0.2"), _folders.ExecutableName)));
    }

    [Fact]
    public async Task Check_EvenNewerVersionAfterADownload_IsOfferedAgain()
    {
        // Arrange
        _isAutomatic = true;
        Latest("2.0.2");
        Updater updater = await StartAsync();

        // Act: the same version a day later, and then a newer one
        _time.Fire();
        await updater.WhenIdleAsync();

        UpdateStatus same = updater.Status;

        _isAutomatic = false;
        Latest("2.0.3");
        _time.Fire();
        await updater.WhenIdleAsync();

        // Assert
        Assert.Equal(new UpdateStatus(UpdateStage.Downloaded, "2.0.1", "2.0.2"), same);
        Assert.Equal(new UpdateStatus(UpdateStage.Available, "2.0.1", "2.0.3"), updater.Status);
    }

    [Fact]
    public async Task Start_RemovesWhatEarlierVersionsLeft()
    {
        // Arrange: this Echo runs from 2.0.1; the link leads to 2.0.2, downloaded before; another Echo still runs 1.9.0
        IVersionFolders system = SystemFolders();

        Directory.Delete(_layout.Current);
        Directory.CreateDirectory(_layout.VersionDirectory("2.0.2"));
        system.PointTo(_layout.Current, _layout.VersionDirectory("2.0.2"));

        Directory.CreateDirectory(_layout.VersionDirectory("1.9.0"));
        Directory.CreateDirectory(_layout.VersionDirectory("2.0.0"));
        Directory.CreateDirectory(_layout.PartialDirectory("2.0.3"));
        File.WriteAllText(Path.Combine(_layout.Versions, "stray.zip"), string.Empty);

        _folders.Running.Add(_layout.VersionDirectory("1.9.0"));
        Latest("2.0.2");

        // Act
        await StartAsync();

        // Assert
        Assert.Equal(
            ["1.9.0", "2.0.1", "2.0.2"],
            Directory.EnumerateFileSystemEntries(_layout.Versions).Select(entry => Path.GetFileName(entry)).Order());
    }

    private void Latest(string version) =>
        _source.Setup(source => source.LatestAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Release(version));

    private static ReleaseInfo Release(string version) => new(
        version,
        "echooo-test.zip",
        new Uri($"https://example.test/v{version}/echooo-test.zip"),
        100,
        new Uri($"https://example.test/v{version}/SHA256SUMS"));

    private Updater Create(bool isInstalled = true)
    {
        var values = new AdditionalValues();
        var settingsStore = new Mock<IUserSettingsStore>();

        settingsStore.Setup(store => store.Load()).Returns(() =>
        {
            values.Set(AdditionalSettings.AutoUpdate, _isAutomatic ? AdditionalSettings.On : AdditionalSettings.Off);

            return new UserSettings { Additional = values };
        });

        var updater = new Updater(
            _source.Object,
            _downloader,
            _archive,
            _folders,
            settingsStore.Object,
            _time,
            isInstalled ? _layout : null,
            "2.0.1",
            _layout.VersionDirectory("2.0.1") + Path.DirectorySeparatorChar);

        updater.Noticed += (_, notice) =>
        {
            lock (_notices)
                _notices.Add(notice);
        };

        return updater;
    }

    private async Task<Updater> StartAsync()
    {
        Updater updater = Create();

        updater.Start(isJustUpdated: false, Task.CompletedTask);
        await updater.WhenIdleAsync();

        return updater;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(10), "The condition did not come true.");
            await Task.Delay(10);
        }
    }

    /// <summary>
    ///     Writes the archive where the real downloader would, after saying it got half of it.
    /// </summary>
    private sealed class FakeDownloader : IFileDownloader
    {
        public int Calls { get; private set; }

        public DownloadRequest? LastRequest { get; private set; }

        /// <summary>
        ///     Holds the download in the middle until it is set.
        /// </summary>
        public TaskCompletionSource? Gate { get; set; }

        public TaskCompletionSource Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? Failure { get; set; }

        public async Task DownloadAsync(DownloadRequest request, string destinationPath, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;

            progress.Report(new DownloadProgress(request.SizeBytes / 2, request.SizeBytes, 0, IsVerifying: false));
            Reported.TrySetResult();

            if (Gate is not null)
                await Gate.Task;

            if (Failure is not null)
                throw Failure;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.WriteAllText(destinationPath, "archive");
        }

        public long RemainingBytes(DownloadRequest request, string destinationPath) => request.SizeBytes;
    }

    /// <summary>
    ///     Unpacks by writing the program file: from the downloaded archive, or "changes" when the server is set to
    ///     give parts of the archive. A server that gives none refuses after a file has been written.
    /// </summary>
    private sealed class FakeArchive : IVersionArchive
    {
        public bool GivesParts { get; set; }

        public bool IsEmpty { get; set; }

        /// <summary>
        ///     The folder the files that stayed the same were taken from.
        /// </summary>
        public string? SameFrom { get; private set; }

        /// <summary>
        ///     Holds the changed files in the middle until it is set.
        /// </summary>
        public TaskCompletionSource? Gate { get; set; }

        public TaskCompletionSource Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Unpack(string archive, string directory)
        {
            Directory.CreateDirectory(directory);

            if (!IsEmpty)
                File.WriteAllText(Path.Combine(directory, "echooo"), File.ReadAllText(archive));
        }

        public void UnpackChanges(ReleaseInfo release, string sameFrom, string directory, IProgress<DownloadProgress> progress)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "half"), string.Empty);

            if (!GivesParts)
                throw new HttpRequestException("example.test did not give bytes 0-65535 of echooo-test.zip: it answered 200.");

            SameFrom = sameFrom;

            progress.Report(new DownloadProgress(250, 1000, 0, IsVerifying: false));
            Reported.TrySetResult();
            Gate?.Task.Wait();

            File.Delete(Path.Combine(directory, "half"));
            File.WriteAllText(Path.Combine(directory, "echooo"), "changes");
        }
    }

    /// <summary>
    ///     Records where the link was pointed.
    /// </summary>
    private sealed class FakeFolders : IVersionFolders
    {
        public List<(string Link, string Directory)> Links { get; } = [];

        /// <summary>
        ///     The folders an Echo runs from, which are not removed.
        /// </summary>
        public List<string> Running { get; } = [];

        public string ArchiveName => "echooo-test.zip";

        public string ExecutableName => "echooo";

        public string InstallCommand => "install";

        public void ApplyAttributes(ZipArchiveEntry entry, string file)
        {
        }

        public void PointTo(string link, string directory) => Links.Add((link, directory));

        public bool TryRemove(string directory)
        {
            if (Running.Contains(directory))
                return false;

            Directory.Delete(directory, recursive: true);

            return true;
        }
    }

    /// <summary>
    ///     A clock that stands still: its one timer goes off when the test says so.
    /// </summary>
    private sealed class ManualTime : TimeProvider
    {
        private TimerCallback? _callback;

        public TimeSpan Period { get; private set; }

        public void Fire() => _callback?.Invoke(null);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            Period = period;

            return new StillTimer();
        }

        private sealed class StillTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
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