namespace Echo.Updates;

/// <summary>
///     A published version of Echo and where its files are.
/// </summary>
/// <param name="Version">The version, such as "2.0.1": the name of its folder once installed.</param>
/// <param name="ArchiveName">The name of the archive for this system, such as "echooo-linux-x64.tar.gz".</param>
/// <param name="ArchiveUrl">Where the archive is.</param>
/// <param name="ArchiveSize">The size of the archive in bytes.</param>
/// <param name="ChecksumsUrl">Where the list of checksums of the release's files is.</param>
internal sealed record ReleaseInfo(string Version, string ArchiveName, Uri ArchiveUrl, long ArchiveSize, Uri ChecksumsUrl);