using System.Net;
using Echo.Updates;

namespace Echo.Tests.UnitTests;

public sealed class GitHubReleaseSourceTests
{
    private const string Checksum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly Uri _latest = new("https://api.example.test/repos/GithubPhobos/Echo/releases/latest");

    [Fact]
    public async Task LatestAsync_Release_GivesItsVersionArchiveAndChecksums()
    {
        // Arrange
        var server = new FakeServer(Release("v2.0.2"));
        var source = new GitHubReleaseSource(new HttpClient(server), _latest, "echooo-linux-x64.tar.gz");

        // Act
        ReleaseInfo release = await source.LatestAsync(CancellationToken.None);

        // Assert: the archive of this system, not of the other one
        Assert.Equal("2.0.2", release.Version);
        Assert.Equal("echooo-linux-x64.tar.gz", release.ArchiveName);
        Assert.Equal(new Uri("https://example.test/v2.0.2/echooo-linux-x64.tar.gz"), release.ArchiveUrl);
        Assert.Equal(116_000_000, release.ArchiveSize);
        Assert.Equal(new Uri("https://example.test/v2.0.2/SHA256SUMS"), release.ChecksumsUrl);

        // The API is asked for its own kind of answer
        Assert.Equal(_latest, server.LastRequest!.RequestUri);
        Assert.Contains("application/vnd.github+json", server.LastRequest.Headers.Accept.ToString());
    }

    [Theory]
    [InlineData("v2.1.0-beta")]
    [InlineData("latest")]
    public async Task LatestAsync_TagThatIsNotAVersion_IsNotARelease(string tag)
    {
        // Arrange
        var source = new GitHubReleaseSource(new HttpClient(new FakeServer(Release(tag))), _latest, "echooo-win-x64.zip");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() => source.LatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LatestAsync_NoArchiveForThisSystem_GivesTheVersionThatCannotBeInstalled()
    {
        // Arrange
        var source = new GitHubReleaseSource(new HttpClient(new FakeServer(Release("v2.0.2"))), _latest, "echooo-osx-arm64.tar.gz");

        // Act
        ReleaseInfo release = await source.LatestAsync(CancellationToken.None);

        // Assert
        Assert.Equal("2.0.2", release.Version);
        Assert.Null(release.ArchiveUrl);
        Assert.False(release.CanBeInstalled);
    }

    [Fact]
    public async Task LatestAsync_ReleaseOfEcho1_GivesItsVersion()
    {
        // Arrange: how the releases of Echo 1.x look, tagged without the "v" and with an archive of another name
        const string answer = """
            {
              "tag_name": "1.0.1",
              "assets": [ { "name": "win-x64.zip", "size": 52000000, "browser_download_url": "https://example.test/1.0.1/win-x64.zip" } ]
            }
            """;

        var source = new GitHubReleaseSource(new HttpClient(new FakeServer(answer)), _latest, "echooo-win-x64.zip");

        // Act
        ReleaseInfo release = await source.LatestAsync(CancellationToken.None);

        // Assert
        Assert.Equal("1.0.1", release.Version);
        Assert.False(release.CanBeInstalled);
    }

    [Fact]
    public async Task ChecksumAsync_ReleaseWithoutTheList_FailsAsUnreadable()
    {
        // Arrange
        var source = new GitHubReleaseSource(new HttpClient(new FakeServer(string.Empty)), _latest, "echooo-win-x64.zip");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() => source.ChecksumAsync(Info() with { ChecksumsUrl = null }, CancellationToken.None));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("""{"tag_name": "v2.0.2", "assets": [{"name": "echooo-win-x64.zip", "size": 1}]}""")]
    public async Task LatestAsync_AnswerThatCannotBeRead_IsNotARelease(string answer)
    {
        // Arrange
        var source = new GitHubReleaseSource(new HttpClient(new FakeServer(answer)), _latest, "echooo-win-x64.zip");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() => source.LatestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LatestAsync_NoReleaseYet_FailsAsARequest()
    {
        // Arrange: GitHub answers "not found" while a repository has no release
        var source = new GitHubReleaseSource(new HttpClient(new FakeServer(string.Empty, HttpStatusCode.NotFound)), _latest, "echooo-win-x64.zip");

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => source.LatestAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData($"{Checksum}  echooo-win-x64.zip\n")]
    [InlineData($"{Checksum} *echooo-win-x64.zip\r\n")]
    [InlineData($"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff  echooo-linux-x64.tar.gz\n{Checksum}  echooo-win-x64.zip")]
    public async Task ChecksumAsync_ListNamingTheArchive_GivesItsChecksum(string checksums)
    {
        // Arrange
        var source = new GitHubReleaseSource(new HttpClient(new FakeServer(checksums)), _latest, "echooo-win-x64.zip");

        // Act
        string checksum = await source.ChecksumAsync(Info(), CancellationToken.None);

        // Assert
        Assert.Equal(Checksum, checksum);
    }

    [Theory]
    [InlineData("")]
    [InlineData($"{Checksum}  echooo-linux-x64.tar.gz\n")]
    [InlineData("12345  echooo-win-x64.zip\n")]
    public async Task ChecksumAsync_ListWithoutTheArchive_FailsAsUnreadable(string checksums)
    {
        // Arrange
        var source = new GitHubReleaseSource(new HttpClient(new FakeServer(checksums)), _latest, "echooo-win-x64.zip");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidDataException>(() => source.ChecksumAsync(Info(), CancellationToken.None));
    }

    [Fact]
    public void Address_VariableSet_PointsAtOtherReleases()
    {
        // Act
        Uri ordinary = GitHubReleaseSource.Address(_ => null);
        Uri moved = GitHubReleaseSource.Address(name => name == GitHubReleaseSource.AddressVariable ? "http://localhost:8080/latest" : null);
        Uri wrong = GitHubReleaseSource.Address(_ => "not an address");

        // Assert
        Assert.Equal(new Uri(GitHubReleaseSource.LatestRelease), ordinary);
        Assert.Equal(new Uri("http://localhost:8080/latest"), moved);
        Assert.Equal(new Uri(GitHubReleaseSource.LatestRelease), wrong);
    }

    private static ReleaseInfo Info() => new(
        "2.0.2",
        "echooo-win-x64.zip",
        new Uri("https://example.test/v2.0.2/echooo-win-x64.zip"),
        1,
        new Uri("https://example.test/v2.0.2/SHA256SUMS"));

    /// <summary>
    ///     A description of a release as the GitHub API gives it, with the files of both systems.
    /// </summary>
    private static string Release(string tag) => $$"""
        {
          "tag_name": "{{tag}}",
          "name": "Echo {{tag}}",
          "assets": [
            { "name": "echooo-win-x64.zip", "size": 118000000, "browser_download_url": "https://example.test/{{tag}}/echooo-win-x64.zip" },
            { "name": "echooo-linux-x64.tar.gz", "size": 116000000, "browser_download_url": "https://example.test/{{tag}}/echooo-linux-x64.tar.gz" },
            { "name": "SHA256SUMS", "size": 180, "browser_download_url": "https://example.test/{{tag}}/SHA256SUMS" }
          ]
        }
        """;

    /// <summary>
    ///     Answers every request with the same text.
    /// </summary>
    private sealed class FakeServer(string answer, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(answer) });
        }
    }
}