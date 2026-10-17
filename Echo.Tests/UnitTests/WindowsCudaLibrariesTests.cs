using System.IO.Compression;
using Echo.Platform.Windows;
using Echo.Setup.Downloads;
using Echo.Setup.Hardware;

namespace Echo.Tests.UnitTests;

public sealed class WindowsCudaLibrariesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly WindowsCudaLibraries _libraries = new();

    public WindowsCudaLibrariesTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Extract_TakesOnlyTheRequestedLibrariesOutOfNestedFolders()
    {
        // Arrange
        string archive = Path.Combine(_directory, "archive.zip");
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "libcublas-archive/bin/x64/cublas64_13.dll", "cublas");
            WriteEntry(zip, "libcublas-archive/bin/x64/nvblas64_13.dll", "not needed");
            WriteEntry(zip, "libcublas-archive/LICENSE", "license");
        }

        string target = Path.Combine(_directory, "cuda");
        Directory.CreateDirectory(target);

        // Act
        _libraries.Extract(archive, Archive(["cublas64_13.dll"]), target);

        // Assert
        Assert.Equal(["cublas64_13.dll"], Directory.GetFiles(target).Select(Path.GetFileName));
        Assert.Equal("cublas", File.ReadAllText(Path.Combine(target, "cublas64_13.dll")));
    }

    [Fact]
    public void Extract_LibraryMissingFromArchive_Throws()
    {
        // Arrange
        string archive = Path.Combine(_directory, "archive.zip");
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            WriteEntry(zip, "readme.txt", "nothing useful");

        // Act & Assert
        Assert.Throws<DownloadException>(() => _libraries.Extract(archive, Archive(["cudart64_13.dll"]), _directory));
    }

    [Fact]
    public void FindMissing_SeesFilesTheUserCopiedIntoTheFolder()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_directory, "cudart64_13.dll"), string.Empty);
        File.WriteAllText(Path.Combine(_directory, "cublas64_13.dll"), string.Empty);

        // Act
        IReadOnlyList<string> missing = _libraries.FindMissing(_directory);

        // Assert: the third library may only be found if a CUDA toolkit is installed on this machine
        Assert.DoesNotContain("cudart64_13.dll", missing);
        Assert.DoesNotContain("cublas64_13.dll", missing);
    }

    [Fact]
    public void Archives_AreFullyDescribed()
    {
        // Assert
        Assert.All(_libraries.Archives, archive =>
        {
            Assert.True(archive.SizeBytes > 0);
            Assert.Matches("^[0-9a-f]{64}$", archive.Sha256);
            Assert.NotEmpty(archive.Libraries);
            Assert.EndsWith(".zip", archive.RelativePath);
        });
    }

    private static CudaArchive Archive(IReadOnlyList<string> libraries) =>
        new("libcublas/windows-x86_64/archive.zip", 1, new string('0', 64), libraries);

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());

        writer.Write(content);
    }
}