namespace Echo.Updates;

/// <summary>
///     What /update did.
/// </summary>
internal enum UpdateStart
{
    /// <summary>The newer version started downloading.</summary>
    Started,

    /// <summary>Nothing is known about a newer version yet: Echo asks now and goes on by itself.</summary>
    Checking,

    /// <summary>The newer version is already being downloaded.</summary>
    AlreadyDownloading,

    /// <summary>The newer version is already downloaded and waits for /restart.</summary>
    AlreadyDownloaded,

    /// <summary>The version in use is the latest one.</summary>
    UpToDate,

    /// <summary>Echo was not installed by the install script, so it cannot replace itself.</summary>
    NotInstalledByScript
}