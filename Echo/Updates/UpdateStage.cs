namespace Echo.Updates;

/// <summary>
///     Where Echo is with a newer version of itself.
/// </summary>
internal enum UpdateStage
{
    /// <summary>Not known yet: the check has not answered, or could not reach the releases.</summary>
    Unknown,

    /// <summary>The version in use is the latest one.</summary>
    Latest,

    /// <summary>A newer version is out and waits for /update.</summary>
    Available,

    /// <summary>A newer version is being downloaded.</summary>
    Downloading,

    /// <summary>A newer version is downloaded and starts with /restart or at the next start.</summary>
    Downloaded
}