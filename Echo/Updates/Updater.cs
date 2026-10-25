using Echo.Listening;
using Echo.Setup;
using Echo.Setup.Downloads;

namespace Echo.Updates;

/// <inheritdoc cref="IUpdater"/>
/// <param name="source">Where the releases are.</param>
/// <param name="downloader">Downloads the whole archive and verifies its checksum.</param>
/// <param name="archive">Unpacks the archive, or makes the new version from the files that changed alone.</param>
/// <param name="folders">How this system keeps the versions.</param>
/// <param name="settingsStore">Holds the user's choice to download new versions by themselves.</param>
/// <param name="time">Times the daily check.</param>
/// <param name="layout">The folders the install script put Echo in; null when it did not, and Echo cannot replace itself.</param>
/// <param name="installed">The version in use.</param>
/// <param name="ownDirectory">The folder this Echo runs from, which is never removed.</param>
internal sealed class Updater(IReleaseSource source,
                              IFileDownloader downloader,
                              IVersionArchive archive,
                              IVersionFolders folders,
                              IUserSettingsStore settingsStore,
                              TimeProvider time,
                              InstallLayout? layout,
                              string installed,
                              string ownDirectory) : IUpdater, IDisposable
{
    /// <summary>
    ///     How often a running Echo asks whether a newer version is out.
    /// </summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    private readonly IReleaseSource _source = source;
    private readonly IFileDownloader _downloader = downloader;
    private readonly IVersionArchive _archive = archive;
    private readonly IVersionFolders _folders = folders;
    private readonly IUserSettingsStore _settingsStore = settingsStore;
    private readonly TimeProvider _time = time;
    private readonly InstallLayout? _layout = layout;
    private readonly string _installed = installed;
    private readonly string _ownDirectory = ownDirectory;
    private readonly object _lock = new();

    private UpdateStatus _status = new(UpdateStage.Unknown, installed);

    /// <summary>
    ///     The newer release the last check found.
    /// </summary>
    private ReleaseInfo? _release;

    private ITimer? _timer;
    private Task _starting = Task.CompletedTask;
    private Task _checking = Task.CompletedTask;
    private Task _downloading = Task.CompletedTask;

    /// <summary>
    ///     Whether /update waits for the check under way, which then tells how it went.
    /// </summary>
    private bool _isCheckAsked;

    /// <inheritdoc/>
    public event EventHandler<UpdateNotice>? Noticed;

    /// <inheritdoc/>
    public UpdateStatus Status
    {
        get
        {
            lock (_lock)
                return _status;
        }
    }

    /// <summary>
    ///     Lines for the log file only: the screen says what matters in the user's language, from the notices.
    /// </summary>
    private static Serilog.ILogger FileLog => Log.ForContext(ScreenLogSink.HiddenProperty, true);

    /// <inheritdoc/>
    public void Start(bool isJustUpdated)
    {
        lock (_lock)
        {
            if (_timer is not null)
                return;

            if (isJustUpdated)
                _status = new UpdateStatus(UpdateStage.Latest, _installed);

            _timer = _time.CreateTimer(_ => Check(isAsked: false), null, CheckInterval, CheckInterval);

            _starting = Task.Run(() =>
            {
                RemoveOldVersions();

                return Check(isAsked: false);
            });
        }
    }

    /// <inheritdoc/>
    public UpdateStart Download()
    {
        lock (_lock)
        {
            switch (_status.Stage)
            {
                case UpdateStage.Downloading:
                    return UpdateStart.AlreadyDownloading;

                case UpdateStage.Downloaded:
                    return UpdateStart.AlreadyDownloaded;

                case UpdateStage.Latest:
                    return UpdateStart.UpToDate;
            }

            if (_layout is null)
                return UpdateStart.NotInstalledByScript;

            if (_status.Stage == UpdateStage.Unknown)
            {
                Check(isAsked: true);

                return UpdateStart.Checking;
            }

            StartDownload(isTold: false);

            return UpdateStart.Started;
        }
    }

    /// <summary>
    ///     Done when no check and no download is under way, including those one of them started. For tests.
    /// </summary>
    internal async Task WhenIdleAsync()
    {
        while (true)
        {
            Task work;

            lock (_lock)
            {
                if (_starting.IsCompleted && _checking.IsCompleted && _downloading.IsCompleted)
                    return;

                work = Task.WhenAll(_starting, _checking, _downloading);
            }

            await work;
        }
    }

    /// <summary>
    ///     Asks for the latest release, unless a check is under way already: then that one is waited for.
    /// </summary>
    /// <param name="isAsked">Whether /update asked, which wants to hear how the check went.</param>
    private Task Check(bool isAsked)
    {
        lock (_lock)
        {
            _isCheckAsked |= isAsked;

            if (_checking.IsCompleted)
                _checking = Task.Run(CheckAsync);

            return _checking;
        }
    }

    private async Task CheckAsync()
    {
        ReleaseInfo latest;

        try
        {
            latest = await _source.LatestAsync(CancellationToken.None);
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            // Without the internet the check simply waits for the next time
            FileLog.Debug("Could not check for a newer Echo: {Reason}", exc.Message);

            if (TakeAsked())
                Notify(new UpdateNotice(UpdateNoticeKind.CheckFailed, _installed, exc.Message));

            return;
        }

        bool isAsked = TakeAsked();
        bool isNewer = ReleaseVersion.IsNewer(latest.Version, _installed);

        // A newer release without the files of this system cannot be installed, so it counts as no answer.
        // An older one, such as a release of Echo 1.x, still tells that the version in use is the latest
        if (isNewer && !latest.CanBeInstalled)
        {
            string reason = $"Echo {latest.Version} is out, but has no {latest.ArchiveName} or no list of checksums.";

            FileLog.Debug("Could not check for a newer Echo: {Reason}", reason);

            if (isAsked)
                Notify(new UpdateNotice(UpdateNoticeKind.CheckFailed, _installed, reason));

            return;
        }

        lock (_lock)
        {
            // What is being downloaded, or is downloaded already, is as new as what the check could find,
            // unless an even newer version has come out since
            if (_status.Stage == UpdateStage.Downloading
                || (_status.Stage == UpdateStage.Downloaded && !ReleaseVersion.IsNewer(latest.Version, _status.Newer!)))
            {
                return;
            }

            _release = isNewer ? latest : null;
            _status = isNewer
                ? new UpdateStatus(UpdateStage.Available, _installed, latest.Version)
                : new UpdateStatus(UpdateStage.Latest, _installed);
        }

        if (!isNewer)
        {
            FileLog.Debug("Echo {Version} is the latest version", _installed);

            if (isAsked)
                Notify(new UpdateNotice(UpdateNoticeKind.UpToDate, _installed));

            return;
        }

        FileLog.Information("Echo {Version} is out", latest.Version);

        // In the manual mode the arrow on the screen and the hint of /update tell about it
        if (_layout is not null && (isAsked || IsAutomatic()))
        {
            lock (_lock)
                StartDownload(isTold: true);
        }
    }

    /// <summary>
    ///     Whether the check under way was asked for by /update; the question is answered once.
    /// </summary>
    private bool TakeAsked()
    {
        lock (_lock)
        {
            bool isAsked = _isCheckAsked;
            _isCheckAsked = false;

            return isAsked;
        }
    }

    /// <summary>
    ///     Starts downloading the newer release. Called under the lock, with the newer release found and Echo installed
    ///     by the script.
    /// </summary>
    /// <param name="isTold">Whether the log is told that it started; /update says that itself.</param>
    private void StartDownload(bool isTold)
    {
        if (_status.Stage != UpdateStage.Available || _release is not { } release || _layout is not { } layout)
            return;

        _status = new UpdateStatus(UpdateStage.Downloading, _installed, release.Version);
        _downloading = Task.Run(() => DownloadAsync(release, layout, isTold));
    }

    private async Task DownloadAsync(ReleaseInfo release, InstallLayout layout, bool isTold)
    {
        // Told from here, so the log always says it before it says how the download ended
        if (isTold)
            Notify(new UpdateNotice(UpdateNoticeKind.DownloadStarted, release.Version));

        string partial = layout.PartialDirectory(release.Version);
        string target = layout.VersionDirectory(release.Version);

        try
        {
            FileLog.Information("Downloading Echo {Version} from {Url}", release.Version, release.ArchiveUrl);

            RemoveDirectory(partial);

            // A folder of the version left by an earlier attempt may be incomplete, so it is made anew;
            // one that an Echo runs from cannot be removed, and is whole
            if (!Directory.Exists(target) || _folders.TryRemove(target))
            {
                if (!TryUnpackChanges(release, target))
                    await DownloadWholeAsync(release, partial, target);

                if (!File.Exists(Path.Combine(target, _folders.ExecutableName)))
                    throw new InvalidDataException($"The archive of Echo {release.Version} has no {_folders.ExecutableName}.");
            }

            // From now on Echo starts in the new version; the running one keeps its own folder
            _folders.PointTo(layout.Current, target);

            RemoveDirectory(partial);

            lock (_lock)
                _status = new UpdateStatus(UpdateStage.Downloaded, _installed, release.Version);

            FileLog.Information("Echo {Version} is downloaded: it starts with /restart or at the next start", release.Version);

            Notify(new UpdateNotice(UpdateNoticeKind.Downloaded, release.Version));
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            FileLog.Warning("Could not download Echo {Version}: {Reason}", release.Version, exc.Message);

            // A folder unpacked only in part is never pointed at; it goes with the next attempt or the next start
            RemoveDirectory(partial);

            lock (_lock)
                _status = new UpdateStatus(UpdateStage.Available, _installed, release.Version);

            Notify(new UpdateNotice(UpdateNoticeKind.DownloadFailed, release.Version, exc.Message));
        }
    }

    /// <summary>
    ///     Makes the folder of the new version from the files of this one that stayed the same and the changed files
    ///     alone, read from the archive on the server. Returns false when that could not be done, as with a server
    ///     that does not give parts of a file: the folder is removed then, for the whole archive.
    /// </summary>
    private bool TryUnpackChanges(ReleaseInfo release, string target)
    {
        try
        {
            _archive.UnpackChanges(release, _ownDirectory, target, new InlineProgress<DownloadProgress>(Report));

            return true;
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            FileLog.Information(
                "Could not download only the changed files of Echo {Version}, so the whole archive is downloaded: {Reason}",
                release.Version,
                exc.Message);

            RemoveDirectory(target);

            return false;
        }
    }

    /// <summary>
    ///     Downloads the whole archive of the release into <paramref name="partial"/>, checks it against the checksum
    ///     the release gives, and unpacks it into <paramref name="target"/>.
    /// </summary>
    private async Task DownloadWholeAsync(ReleaseInfo release, string partial, string target)
    {
        string checksum = await _source.ChecksumAsync(release, CancellationToken.None);
        string archive = Path.Combine(partial, release.ArchiveName);

        // Only a release that can be installed is ever offered, so its archive is there
        await _downloader.DownloadAsync(
            new DownloadRequest(release.ArchiveUrl!, release.ArchiveSize, checksum),
            archive,
            new InlineProgress<DownloadProgress>(Report),
            CancellationToken.None);

        _archive.Unpack(archive, target);
    }

    private void Report(DownloadProgress progress)
    {
        lock (_lock)
        {
            if (_status.Stage == UpdateStage.Downloading && progress.BytesTotal > 0)
                _status = _status with { Progress = (double)progress.BytesDone / progress.BytesTotal };
        }
    }

    /// <summary>
    ///     Removes what earlier versions left in the versions folder: their folders, and downloads that never finished.
    ///     The folder this Echo runs from and the one "current" leads to stay; a folder another Echo still runs from
    ///     is removed at a later start.
    /// </summary>
    private void RemoveOldVersions()
    {
        if (_layout is null || !Directory.Exists(_layout.Versions))
            return;

        string? current = _layout.CurrentDirectory();

        try
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(_layout.Versions).ToList())
            {
                if (IsSame(entry, _ownDirectory) || (current is not null && IsSame(entry, current)))
                    continue;

                bool isGone = Directory.Exists(entry) ? _folders.TryRemove(entry) : TryDeleteFile(entry);

                if (isGone)
                    FileLog.Debug("Removed {Entry}, left by an earlier version", entry);
                else
                    FileLog.Debug("{Entry} is still in use; it is removed at a later start", entry);
            }
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            FileLog.Debug("Could not look through {Folder}: {Reason}", _layout.Versions, exc.Message);
        }
    }

    private bool IsAutomatic()
    {
        try
        {
            return _settingsStore.Load().Additional.IsOn(AdditionalSettings.AutoUpdate);
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void Notify(UpdateNotice notice) =>
        Noticed?.Invoke(this, notice);

    private static bool IsSame(string first, string second) =>
        PathNames.AreSame(Path.TrimEndingDirectorySeparator(first), Path.TrimEndingDirectorySeparator(second));

    private static void RemoveDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            // Whatever stays is removed at the next start
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);

            return true;
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
            _timer?.Dispose();
    }
}