namespace Echo.Updates;

/// <summary>
///     What the updater has to tell the user in the log.
/// </summary>
internal enum UpdateNoticeKind
{
    /// <summary>A newer version started downloading by itself, or after /update asked for a check.</summary>
    DownloadStarted,

    /// <summary>The newer version is downloaded.</summary>
    Downloaded,

    /// <summary>The newer version could not be downloaded.</summary>
    DownloadFailed,

    /// <summary>The check /update asked for found no newer version.</summary>
    UpToDate,

    /// <summary>The check /update asked for could not reach the releases.</summary>
    CheckFailed
}