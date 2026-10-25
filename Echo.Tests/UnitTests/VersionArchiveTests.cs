using System.IO.Compression;
using System.Text;
using Echo.Setup.Downloads;
using Echo.Updates;

namespace Echo.Tests.UnitTests;

public sealed class VersionArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly Mock<IVersionFolders> _folders = new();

    public VersionArchiveTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Unpack_DownloadedArchive_PutsItsFilesIntoTheFolder()
    {
        // Arrange
        string archive = Path.Combine(_root, "echooo-test.zip");
        File.WriteAllBytes(archive, Archive(("echooo", Text("2.0.2")), ("Assets/sound.wav", Text("{}"))));

        string target = Path.Combine(_root, "2.0.2");

        // Act
        Create(new RangeServer([])).Unpack(archive, target);

        // Assert
        Assert.Equal("2.0.2", File.ReadAllText(Path.Combine(target, "echooo")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "Assets", "sound.wav")));
        _folders.Verify(folders => folders.ApplyAttributes(It.IsAny<ZipArchiveEntry>(), Path.Combine(target, "echooo")));
    }

    [Fact]
    public void UnpackChanges_SomeFilesChanged_DownloadsOnlyThoseAndCopiesTheRest()
    {
        // Arrange: the program is as large as before, yet different; the library is the same; a sound is new,
        // and a library of the version in use is gone from the new one
        byte[] library = Noise(2_000_000);
        string inUse = Folder("2.0.1", ("echooo", Text("2.0.1")), ("library.dll", library), ("gone.dll", Text("old")));
        byte[] archive = Archive(("echooo", Text("2.0.2")), ("library.dll", library), ("Assets/new.wav", Text("{}")));

        var server = new RangeServer(archive);
        var progress = new List<DownloadProgress>();
        string target = Path.Combine(_root, "versions", "2.0.2");

        // Act
        Create(server).UnpackChanges(Release(archive), inUse, target, new InlineProgress<DownloadProgress>(progress.Add));

        // Assert
        Assert.Equal("2.0.2", File.ReadAllText(Path.Combine(target, "echooo")));
        Assert.Equal(library, File.ReadAllBytes(Path.Combine(target, "library.dll")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(target, "Assets", "new.wav")));
        Assert.False(File.Exists(Path.Combine(target, "gone.dll")));

        // The library of 2 MB was not downloaded: the end of the archive and the start, 64 KB each, were
        Assert.Equal(2 * RemoteFileStream.SmallestPart, server.BytesSent);

        DownloadProgress last = progress[^1];

        Assert.Equal(last.BytesTotal, last.BytesDone);
        _folders.Verify(folders => folders.ApplyAttributes(It.IsAny<ZipArchiveEntry>(), It.IsAny<string>()), Times.Exactly(3));
    }

    [Fact]
    public void UnpackChanges_ServerDoesNotGiveParts_Throws()
    {
        // Arrange
        byte[] archive = Archive(("echooo", Text("2.0.2")));
        string inUse = Folder("2.0.1", ("echooo", Text("2.0.1")));
        var server = new RangeServer(archive) { IgnoresRanges = true };

        // Act & Assert
        Assert.Throws<HttpRequestException>(() => Create(server).UnpackChanges(
            Release(archive), inUse, Path.Combine(_root, "versions", "2.0.2"), new Progress<DownloadProgress>()));
    }

    [Fact]
    public void UnpackChanges_ChangedFileDamagedOnTheServer_Throws()
    {
        // Arrange: the table of contents gives the program another checksum than what it holds, as when a byte
        // of it was damaged; the checksum is 16 bytes into the program's entry, which starts with "PK", 1, 2
        byte[] archive = Archive(("echooo", Noise(100_000)));
        string inUse = Folder("2.0.1", ("echooo", Text("2.0.1")));

        archive[archive.AsSpan().LastIndexOf("PK"u8) + 16] ^= 0xFF;

        // Act & Assert
        Assert.Throws<InvalidDataException>(() => Create(new RangeServer(archive)).UnpackChanges(
            Release(archive), inUse, Path.Combine(_root, "versions", "2.0.2"), new Progress<DownloadProgress>()));
    }

    [Fact]
    public void UnpackChanges_NameOutsideTheFolder_Throws()
    {
        // Arrange
        byte[] archive = Archive(("../outside.txt", Text("no")));
        string inUse = Folder("2.0.1", ("echooo", Text("2.0.1")));

        // Act
        Assert.Throws<InvalidDataException>(() => Create(new RangeServer(archive)).UnpackChanges(
            Release(archive), inUse, Path.Combine(_root, "versions", "2.0.2"), new Progress<DownloadProgress>()));

        // Assert
        Assert.False(File.Exists(Path.Combine(_root, "versions", "outside.txt")));
    }

    private VersionArchive Create(RangeServer server) =>
        new(new HttpClient(server), _folders.Object);

    private static ReleaseInfo Release(byte[] archive) => new(
        "2.0.2",
        "echooo-test.zip",
        new Uri("https://example.test/v2.0.2/echooo-test.zip"),
        archive.Length,
        new Uri("https://example.test/v2.0.2/SHA256SUMS"));

    /// <summary>
    ///     The folder of a version that holds these files.
    /// </summary>
    private string Folder(string version, params (string Name, byte[] Contents)[] files)
    {
        string directory = Path.Combine(_root, "versions", version);

        foreach ((string name, byte[] contents) in files)
        {
            string file = Path.Combine(directory, name);

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, contents);
        }

        return directory;
    }

    private static byte[] Archive(params (string Name, byte[] Contents)[] files)
    {
        var archive = new MemoryStream();

        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create))
        {
            foreach ((string name, byte[] contents) in files)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(contents);
            }
        }

        return archive.ToArray();
    }

    private static byte[] Text(string text) =>
        Encoding.UTF8.GetBytes(text);

    /// <summary>
    ///     Bytes that cannot be compressed, so a file of them is as large in the archive as on the disk.
    /// </summary>
    private static byte[] Noise(int size)
    {
        byte[] noise = new byte[size];
        new Random(1).NextBytes(noise);

        return noise;
    }
}