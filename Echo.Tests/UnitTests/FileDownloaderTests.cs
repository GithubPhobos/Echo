using System.Net;
using System.Security.Cryptography;
using Echo.Setup.Downloads;

namespace Echo.Tests.UnitTests;

public sealed class FileDownloaderTests : IDisposable
{
    /// <summary>
    ///     How long the server may send nothing, ample for a server that answers at once.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    public FileDownloaderTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task DownloadAsync_WholeFile_IsVerifiedAndMovedIntoPlace()
    {
        // Arrange
        byte[] content = RandomNumberGenerator.GetBytes(300_000);
        string target = Path.Combine(_directory, "model.bin");
        var downloader = new FileDownloader(new HttpClient(new FakeServer(content)), _patience);

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
        var downloader = new FileDownloader(new HttpClient(server), _patience);

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
        var downloader = new FileDownloader(new HttpClient(new FakeServer(content)), _patience);
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
        var downloader = new FileDownloader(new HttpClient(new FakeServer(null)), _patience);
        var request = new DownloadRequest(new Uri("https://example.test/model.bin"), 10, new string('0', 64));

        // Act & Assert
        await Assert.ThrowsAsync<DownloadException>(() => downloader.DownloadAsync(
            request, Path.Combine(_directory, "model.bin"), new InlineProgress<DownloadProgress>(_ => { }), CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_ServerStopsSending_GivesUpAndKeepsWhatCame()
    {
        // Arrange: the server sends half of the file and then nothing, keeping the connection open
        byte[] content = RandomNumberGenerator.GetBytes(300_000);
        string target = Path.Combine(_directory, "model.bin");
        var downloader = new FileDownloader(new HttpClient(new FakeServer(content) { Stalls = true }), TimeSpan.FromMilliseconds(300));

        // Act
        var exc = await Assert.ThrowsAsync<DownloadException>(() => downloader.DownloadAsync(
            Request(content), target, new InlineProgress<DownloadProgress>(_ => { }), CancellationToken.None));

        // Assert: the next attempt goes on from there
        Assert.Contains("example.test sent nothing for", exc.Message);
        Assert.Equal(150_000, new FileInfo(target + ".part").Length);
    }

    [Fact]
    public async Task DownloadAsync_ServerSendsSlowlyButOn_WaitsForTheWholeFile()
    {
        // Arrange: every piece comes well before the server would be given up on, the whole file later than that
        byte[] content = RandomNumberGenerator.GetBytes(1000);
        string target = Path.Combine(_directory, "model.bin");
        var clock = new DownloadClock();
        var server = new FakeServer(content) { Pause = TimeSpan.FromMilliseconds(250), Advance = clock.Advance };
        var downloader = new FileDownloader(new HttpClient(server), TimeSpan.FromMilliseconds(700), clock);

        // Act
        await downloader.DownloadAsync(Request(content), target, new InlineProgress<DownloadProgress>(_ => { }), CancellationToken.None);

        // Assert
        Assert.Equal(content, File.ReadAllBytes(target));
        Assert.True(clock.Elapsed > TimeSpan.FromMilliseconds(700));
    }

    [Fact]
    public async Task DownloadAsync_CancelledWhileTheServerSendsNothing_IsCancelled()
    {
        // Arrange
        byte[] content = RandomNumberGenerator.GetBytes(300_000);
        var downloader = new FileDownloader(new HttpClient(new FakeServer(content) { Stalls = true }), _patience);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Act & Assert: the user's Esc, not a stalled server
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(
            Request(content), Path.Combine(_directory, "model.bin"), new InlineProgress<DownloadProgress>(_ => { }), cancellation.Token));
    }

    [Fact]
    public void RemainingBytes_PartialFileFromEarlierAttempt_CountsOnlyTheRest()
    {
        // Arrange
        byte[] content = RandomNumberGenerator.GetBytes(300_000);
        string target = Path.Combine(_directory, "model.bin");
        File.WriteAllBytes(target + ".part", content[..100_000]);

        var downloader = new FileDownloader(new HttpClient(new FakeServer(content)), _patience);

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

        var downloader = new FileDownloader(new HttpClient(new FakeServer(content)), _patience);

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

        /// <summary>
        ///     The pause before each of the four pieces the file is sent in.
        /// </summary>
        public TimeSpan Pause { get; init; }

        public Action<TimeSpan>? Advance { get; init; }

        /// <summary>
        ///     Whether the server sends half of the file and then nothing, keeping the connection open.
        /// </summary>
        public bool Stalls { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (content is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            LastRangeStart = request.Headers.Range?.Ranges.First().From;

            int start = (int)(LastRangeStart ?? 0);
            byte[] rest = content[start..];

            return Task.FromResult(new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = Stalls || Pause > TimeSpan.Zero ? new StreamContent(new SlowBody(rest, Pause, Stalls, Advance)) : new ByteArrayContent(rest)
            });
        }
    }
}