using System.Net;
using System.Security.Cryptography;
using Echo.Setup.Downloads;

namespace Echo.Tests.UnitTests;

public sealed class FileDownloaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    public FileDownloaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task DownloadAsync_WholeFile_IsVerifiedAndMovedIntoPlace()
    {
        // Arrange
        byte[] content = RandomNumberGenerator.GetBytes(300_000);
        string target = Path.Combine(_directory, "model.bin");
        var downloader = new FileDownloader(new HttpClient(new FakeServer(content)));

        // Act
        await downloader.DownloadAsync(Request(content), target, new InlineProgress<DownloadProgress>(_ => { }), CancellationToken.None);

        // Assert
        Assert.Equal(content, File.ReadAllBytes(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task DownloadAsync_PartialFileFromEarlierAttempt_AsksOnlyForTheRest()
    {
        // Arrange
        byte[] content = RandomNumberGenerator.GetBytes(300_000);
        string target = Path.Combine(_directory, "model.bin");
        File.WriteAllBytes(target + ".part", content[..100_000]);

        var server = new FakeServer(content);
        var downloader = new FileDownloader(new HttpClient(server));

        // Act
        await downloader.DownloadAsync(Request(content), target, new InlineProgress<DownloadProgress>(_ => { }), CancellationToken.None);

        // Assert
        Assert.Equal(100_000, server.LastRangeStart);
        Assert.Equal(content, File.ReadAllBytes(target));
    }

    [Fact]
    public async Task DownloadAsync_ChecksumMismatch_ThrowsAndLeavesNothingBehind()
    {
        // Arrange
        byte[] content = RandomNumberGenerator.GetBytes(50_000);
        string target = Path.Combine(_directory, "model.bin");
        var downloader = new FileDownloader(new HttpClient(new FakeServer(content)));
        var request = new DownloadRequest(new Uri("https://example.test/model.bin"), content.Length, new string('0', 64));

        // Act & Assert
        await Assert.ThrowsAsync<DownloadException>(() => downloader.DownloadAsync(
            request, target, new InlineProgress<DownloadProgress>(_ => { }), CancellationToken.None));

        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task DownloadAsync_ServerRefuses_ThrowsDownloadException()
    {
        // Arrange
        var downloader = new FileDownloader(new HttpClient(new FakeServer(null)));
        var request = new DownloadRequest(new Uri("https://example.test/model.bin"), 10, new string('0', 64));

        // Act & Assert
        await Assert.ThrowsAsync<DownloadException>(() => downloader.DownloadAsync(
            request, Path.Combine(_directory, "model.bin"), new InlineProgress<DownloadProgress>(_ => { }), CancellationToken.None));
    }

    [Fact]
    public void RemainingBytes_PartialFileFromEarlierAttempt_CountsOnlyTheRest()
    {
        // Arrange
        byte[] content = RandomNumberGenerator.GetBytes(300_000);
        string target = Path.Combine(_directory, "model.bin");
        File.WriteAllBytes(target + ".part", content[..100_000]);

        var downloader = new FileDownloader(new HttpClient(new FakeServer(content)));

        // Act
        long remaining = downloader.RemainingBytes(Request(content), target);

        // Assert
        Assert.Equal(200_000, remaining);
    }

    [Fact]
    public void RemainingBytes_PartialFileLongerThanTheDownload_CountsTheWholeFile()
    {
        // Arrange: such a file belongs to another download, and is deleted before this one starts
        byte[] content = RandomNumberGenerator.GetBytes(300_000);
        string target = Path.Combine(_directory, "model.bin");
        File.WriteAllBytes(target + ".part", RandomNumberGenerator.GetBytes(400_000));

        var downloader = new FileDownloader(new HttpClient(new FakeServer(content)));

        // Act
        long remaining = downloader.RemainingBytes(Request(content), target);

        // Assert
        Assert.Equal(300_000, remaining);
    }

    private static DownloadRequest Request(byte[] content) =>
        new(new Uri("https://example.test/model.bin"), content.Length, Convert.ToHexString(SHA256.HashData(content)));

    /// <summary>
    ///     Serves one file and honors range requests, the way the real download servers do.
    /// </summary>
    private sealed class FakeServer(byte[]? content) : HttpMessageHandler
    {
        public long? LastRangeStart { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (content is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            LastRangeStart = request.Headers.Range?.Ranges.First().From;

            int start = (int)(LastRangeStart ?? 0);

            return Task.FromResult(new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content, start, content.Length - start)
            });
        }
    }
}