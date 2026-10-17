using System.Diagnostics;
using System.Runtime.Versioning;
using Echo.Platform.Linux;
using Echo.Setup.Downloads;
using Echo.Setup.Hardware;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class LinuxCudaLibrariesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly LinuxCudaLibraries _libraries = new(new LinuxPrograms(Environment.GetEnvironmentVariable));

    public LinuxCudaLibrariesTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Archives_AreTheLinuxOnesFullyDescribed()
    {
        // Assert
        Assert.All(_libraries.Archives, archive =>
        {
            Assert.True(archive.SizeBytes > 0);
            Assert.Matches("^[0-9a-f]{64}$", archive.Sha256);
            Assert.EndsWith(".tar.xz", archive.RelativePath);
            Assert.Contains("linux-x86_64", archive.RelativePath);
        });
        Assert.Equal(["libcudart.so.13", "libcublasLt.so.13", "libcublas.so.13"], _libraries.Archives.SelectMany(archive => archive.Libraries));
    }

    [Fact]
    public void ExtractAndMoveIntoPlace_ArchiveLikeNvidias_KeepsTheVersionedFileAndItsShortName()
    {
        if (!OperatingSystem.IsLinux() || _libraries.MissingTools().Count > 0)
            return;

        // Arrange: an archive laid out the way NVIDIA's are, with a static library that is not wanted
        string archivePath = CreateArchive(
            ("cuda_cudart-archive/lib/libcudart.so.13.4.92", "the library"),
            ("cuda_cudart-archive/lib/libcudart_static.a", "not needed"),
            ("cuda_cudart-archive/include/cuda.h", "not needed"));

        string staging = Path.Combine(_directory, "staging");
        string target = Path.Combine(_directory, "cuda");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(target);

        CudaArchive archive = Archive(["libcudart.so.13"]);

        // Act
        _libraries.Extract(archivePath, archive, staging);
        _libraries.MoveIntoPlace(staging, "libcudart.so.13", target);

        // Assert: the file under its full version, and the short name the loader asks for pointing at it
        Assert.Equal("the library", File.ReadAllText(Path.Combine(target, "libcudart.so.13.4.92")));
        Assert.Equal("libcudart.so.13.4.92", new FileInfo(Path.Combine(target, "libcudart.so.13")).LinkTarget);
        Assert.Equal("the library", File.ReadAllText(Path.Combine(target, "libcudart.so.13")));
        Assert.Equal(["libcudart.so.13", "libcudart.so.13.4.92"], Directory.GetFileSystemEntries(target).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Extract_LibraryMissingFromArchive_Throws()
    {
        if (!OperatingSystem.IsLinux() || _libraries.MissingTools().Count > 0)
            return;

        // Arrange
        string archivePath = CreateArchive(("archive/LICENSE", "nothing useful"));

        // Act & Assert
        Assert.Throws<DownloadException>(() => _libraries.Extract(archivePath, Archive(["libcublas.so.13"]), _directory));
    }

    [Fact]
    public void FindMissing_LibrariesCopiedIntoTheFolder_AreNotMissing()
    {
        if (!OperatingSystem.IsLinux())
            return;

        // Arrange
        foreach (string library in new[] { "libcudart.so.13", "libcublasLt.so.13", "libcublas.so.13" })
            File.WriteAllText(Path.Combine(_directory, library), string.Empty);

        // Act & Assert
        Assert.Empty(_libraries.FindMissing(_directory));
    }

    private static CudaArchive Archive(IReadOnlyList<string> libraries) =>
        new("cuda_cudart/linux-x86_64/archive.tar.xz", 1, new string('0', 64), libraries);

    /// <summary>
    ///     Makes a 'tar.xz' with the files and, for a library with a version, the link from its short name.
    /// </summary>
    private string CreateArchive(params (string Path, string Content)[] files)
    {
        string source = Path.Combine(_directory, "source");

        foreach ((string path, string content) in files)
        {
            string full = Path.Combine(source, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);

            // libcudart.so.13.4.92 gets its link libcudart.so.13
            string name = Path.GetFileName(path);
            int version = name.IndexOf(".so.", StringComparison.Ordinal);

            if (version > 0 && name.Count(character => character == '.') > 2)
                File.CreateSymbolicLink(Path.Combine(Path.GetDirectoryName(full)!, name[..(version + 6)]), name);
        }

        string archivePath = Path.Combine(_directory, "archive.tar.xz");

        using Process tar = Process.Start(new ProcessStartInfo("tar", ["-cJf", archivePath, "-C", source, "."]) { UseShellExecute = false })!;
        tar.WaitForExit();

        Directory.Delete(source, recursive: true);

        return archivePath;
    }
}