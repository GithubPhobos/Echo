using System.IO.Compression;
using System.IO.Hashing;
using Echo.Listening;
using Echo.Setup.Downloads;

namespace Echo.Updates;

/// <inheritdoc cref="IVersionArchive"/>
/// <param name="httpClient">The client the parts of an archive on the server are downloaded with.</param>
/// <param name="folders">Gives an unpacked file what this system keeps about it besides its contents.</param>
internal sealed class VersionArchive(HttpClient httpClient, IVersionFolders folders) : IVersionArchive
{
    private const int BufferSize = 128 * 1024;

    private readonly HttpClient _httpClient = httpClient;
    private readonly IVersionFolders _folders = folders;

    /// <summary>
    ///     Lines for the log file only.
    /// </summary>
    private static Serilog.ILogger FileLog => Log.ForContext(ScreenLogSink.HiddenProperty, true);

    /// <inheritdoc/>
    public void Unpack(string archive, string directory)
    {
        Directory.CreateDirectory(directory);

        using ZipArchive zip = ZipFile.OpenRead(archive);

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (Target(entry, directory) is { } file)
                Extract(entry, file, () => { });
        }
    }

    /// <inheritdoc/>
    public void UnpackChanges(ReleaseInfo release, string sameFrom, string directory, IProgress<DownloadProgress> progress)
    {
        Directory.CreateDirectory(directory);

        using var remote = new RemoteFileStream(_httpClient, release.ArchiveUrl!, release.ArchiveSize);
        using var zip = new ZipArchive(remote, ZipArchiveMode.Read);

        List<(ZipArchiveEntry Entry, string File)> changed = [];
        int count = 0;

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (Target(entry, directory) is not { } file)
                continue;

            count++;

            string same = Path.Combine(sameFrom, Path.GetRelativePath(directory, file));

            if (IsSame(entry, same))
            {
                File.Copy(same, file);
                _folders.ApplyAttributes(entry, file);
            }
            else
            {
                changed.Add((entry, file));
            }
        }

        // From here on only the changed files are downloaded, with a little of the archive around each
        long start = remote.Downloaded;
        long total = changed.Sum(item => item.Entry.CompressedLength);

        void Report() =>
            progress.Report(new DownloadProgress(Math.Min(remote.Downloaded - start, total), total, 0, IsVerifying: false));

        foreach ((ZipArchiveEntry entry, string file) in changed)
            Extract(entry, file, Report);

        FileLog.Information(
            "Downloaded {Changed} of {Count} files, {Bytes} bytes; the others are the same as in {Folder}",
            changed.Count,
            count,
            remote.Downloaded,
            sameFrom);
    }

    /// <summary>
    ///     Where a file of the archive goes in <paramref name="directory"/>, with the folders on the way made;
    ///     null for an entry that is a folder, which is made itself.
    /// </summary>
    /// <exception cref="InvalidDataException">The entry would go outside the folder.</exception>
    private static string? Target(ZipArchiveEntry entry, string directory)
    {
        string path = Path.GetFullPath(Path.Combine(directory, entry.FullName));

        // A name such as "../../.bashrc" must not reach out of the folder
        if (!PathNames.IsInside(path, directory))
            throw new InvalidDataException($"The archive names {entry.FullName}, which lies outside the folder of the version.");

        // The name of a folder's entry ends with a slash
        if (entry.Name.Length == 0)
        {
            Directory.CreateDirectory(path);

            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        return path;
    }

    /// <summary>
    ///     Whether <paramref name="file"/> holds what the entry holds: it is as large, and its checksum is the same.
    /// </summary>
    private static bool IsSame(ZipArchiveEntry entry, string file)
    {
        var info = new FileInfo(file);

        if (!info.Exists || info.Length != entry.Length)
            return false;

        var checksum = new Crc32();

        // The files of the running Echo are open, and its program file is in use: reading them is still allowed
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize))
            checksum.Append(stream);

        return checksum.GetCurrentHashAsUInt32() == entry.Crc32;
    }

    /// <summary>
    ///     Writes what the entry holds to <paramref name="file"/> and checks it against the entry's checksum.
    /// </summary>
    /// <param name="written">Called after every piece written.</param>
    /// <exception cref="InvalidDataException">What was read does not match the checksum.</exception>
    private void Extract(ZipArchiveEntry entry, string file, Action written)
    {
        var checksum = new Crc32();
        byte[] buffer = new byte[BufferSize];

        using (Stream source = entry.Open())
        using (FileStream target = File.Create(file))
        {
            int read;

            while ((read = source.Read(buffer)) > 0)
            {
                target.Write(buffer, 0, read);
                checksum.Append(buffer.AsSpan(0, read));
                written();
            }
        }

        if (checksum.GetCurrentHashAsUInt32() != entry.Crc32)
            throw new InvalidDataException($"{entry.FullName} in the archive is damaged: its checksum does not match.");

        _folders.ApplyAttributes(entry, file);
    }
}