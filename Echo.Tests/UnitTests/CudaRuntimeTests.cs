using System.IO.Compression;
using Echo.Setup.Downloads;
using Echo.Setup.Hardware;

namespace Echo.Tests.UnitTests;

public sealed class CudaRuntimeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    public CudaRuntimeTests() => Directory.CreateDirectory(_directory);

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
        CudaRuntime.Extract(archive, ["cublas64_13.dll"], target);

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
        Assert.Throws<DownloadException>(() => CudaRuntime.Extract(archive, ["cudart64_13.dll"], _directory));
    }

    [Fact]
    public void FindMissingLibraries_SeesFilesTheUserCopiedIntoTheFolder()
    {
        // Arrange
        var paths = new AppPaths(_directory, _directory);
        var runtime = new CudaRuntime(paths, Mock.Of<IFileDownloader>());

        Directory.CreateDirectory(paths.CudaDirectory);
        File.WriteAllText(Path.Combine(paths.CudaDirectory, "cudart64_13.dll"), string.Empty);
        File.WriteAllText(Path.Combine(paths.CudaDirectory, "cublas64_13.dll"), string.Empty);

        // Act
        IReadOnlyList<string> missing = runtime.FindMissingLibraries();

        // Assert: the third library may only be found if a CUDA toolkit is installed on this machine
        Assert.DoesNotContain("cudart64_13.dll", missing);
        Assert.DoesNotContain("cublas64_13.dll", missing);
    }

    [Fact]
    public async Task InstallAsync_PutsAllLibrariesInPlaceAndCleansUp()
    {
        // Arrange
        var paths = new AppPaths(_directory, _directory);
        var runtime = new CudaRuntime(paths, CreateArchiveDownloader(failingArchiveIndex: null).Object);

        var downloaded = new List<string>();

        // Act
        await runtime.InstallAsync(
            new InlineProgress<DownloadProgress>(_ => { }), new InlineProgress<string>(downloaded.Add), CancellationToken.None);

        // Assert
        Assert.Equal(
            ["cublas64_13.dll", "cublasLt64_13.dll", "cudart64_13.dll"],
            Directory.GetFiles(paths.CudaDirectory).Select(Path.GetFileName).Order());
        Assert.Empty(Directory.GetDirectories(paths.CudaDirectory));

        // Every library is announced as its archive arrives, in the order of the archives
        Assert.Equal(runtime.Libraries, downloaded);
    }

    [Fact]
    public async Task InstallAsync_SecondArchiveFails_KeepsWhatArrivedAndDoesNotDownloadItAgain()
    {
        // Arrange
        var paths = new AppPaths(_directory, _directory);
        var failing = new CudaRuntime(paths, CreateArchiveDownloader(failingArchiveIndex: 1).Object);

        Mock<IFileDownloader> working = CreateArchiveDownloader(failingArchiveIndex: null);
        var downloaded = new List<string>();

        // Act: the first attempt loses the connection on the second archive, the next one finishes the job
        await Assert.ThrowsAsync<DownloadException>(() => failing.InstallAsync(
            new InlineProgress<DownloadProgress>(_ => { }), new InlineProgress<string>(_ => { }), CancellationToken.None));

        string[] afterFailure = [.. Directory.GetFiles(paths.CudaDirectory).Select(Path.GetFileName)!];
        IReadOnlyList<string> missingAfterFailure = failing.FindMissingLibraries();

        await new CudaRuntime(paths, working.Object).InstallAsync(
            new InlineProgress<DownloadProgress>(_ => { }), new InlineProgress<string>(downloaded.Add), CancellationToken.None);

        // Assert: the library that arrived is in place; the incomplete set is still reported as incomplete,
        // which is what keeps the Whisper runtime away from CUDA
        Assert.Equal(["cudart64_13.dll"], afterFailure);
        Assert.Contains("cublas64_13.dll", missingAfterFailure);

        // Only the archive that was lost is asked for again
        Assert.Equal(["cublas64_13.dll", "cublasLt64_13.dll"], downloaded);

        working.Verify(
            mock => mock.DownloadAsync(
                It.Is<DownloadRequest>(request => request.Url.AbsoluteUri.Contains("cuda_cudart")),
                It.IsAny<string>(),
                It.IsAny<IProgress<DownloadProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        Assert.Equal(3, Directory.GetFiles(paths.CudaDirectory).Length);
        Assert.Empty(Directory.GetDirectories(paths.CudaDirectory));
    }

    [Fact]
    public void Archives_AreFullyDescribed()
    {
        // Assert
        Assert.All(CudaRuntime.Archives, archive =>
        {
            Assert.True(archive.SizeBytes > 0);
            Assert.Matches("^[0-9a-f]{64}$", archive.Sha256);
            Assert.NotEmpty(archive.Libraries);
            Assert.EndsWith(".zip", archive.RelativePath);
        });
    }

    [Fact]
    public void RequiredSpaceBytes_LibrariesOfAnArchiveInPlace_CountsOnlyWhatIsStillToCome()
    {
        // Arrange: the runtime library is in place, and a part of the other archive has already arrived
        var paths = new AppPaths(_directory, _directory);
        var downloader = new Mock<IFileDownloader>();

        downloader.Setup(d => d.RemainingBytes(It.IsAny<DownloadRequest>(), It.IsAny<string>())).Returns(1_000);

        Directory.CreateDirectory(paths.CudaDirectory);
        File.WriteAllText(Path.Combine(paths.CudaDirectory, "cudart64_13.dll"), string.Empty);

        var runtime = new CudaRuntime(paths, downloader.Object);

        // Act
        long required = runtime.RequiredSpaceBytes();

        // Assert: nothing for the archive whose library is in place; what is left of the other one and its
        // unpacked libraries, unless a CUDA toolkit on this machine already has them
        CudaArchive libraries = CudaRuntime.Archives[1];
        bool isFoundElsewhere = !runtime.FindMissingLibraries().Any(libraries.Libraries.Contains);

        Assert.Equal(isFoundElsewhere ? 0 : 1_000 + (long)(libraries.SizeBytes * CudaRuntime.UnpackedSizeFactor), required);
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());

        writer.Write(content);
    }

    /// <summary>
    ///     A downloader that "downloads" archives holding exactly the libraries the real ones hold.
    /// </summary>
    private static Mock<IFileDownloader> CreateArchiveDownloader(int? failingArchiveIndex)
    {
        var downloader = new Mock<IFileDownloader>();

        downloader
            .Setup(mock => mock.DownloadAsync(
                It.IsAny<DownloadRequest>(), It.IsAny<string>(), It.IsAny<IProgress<DownloadProgress>>(), It.IsAny<CancellationToken>()))
            .Returns((DownloadRequest request, string destination, IProgress<DownloadProgress> _, CancellationToken _) =>
            {
                int index = CudaRuntime.Archives.ToList().FindIndex(archive => request.Url.AbsoluteUri.EndsWith(archive.RelativePath));

                if (index == failingArchiveIndex)
                    throw new DownloadException("The connection was lost.");

                using ZipArchive zip = ZipFile.Open(destination, ZipArchiveMode.Create);

                foreach (string library in CudaRuntime.Archives[index].Libraries)
                    WriteEntry(zip, $"archive/bin/x64/{library}", library);

                return Task.CompletedTask;
            });

        return downloader;
    }
}