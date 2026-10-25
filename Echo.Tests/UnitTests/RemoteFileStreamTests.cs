using Echo.Updates;

namespace Echo.Tests.UnitTests;

public sealed class RemoteFileStreamTests
{
    private static readonly Uri _address = new("https://example.test/v2.0.2/echooo-test.zip");

    /// <summary>
    ///     How long the server may send nothing, ample for a server that answers at once.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void Read_HereAndThere_GivesTheBytesOfThatPlace()
    {
        // Arrange
        byte[] content = Content(1_000_000);
        using var stream = new RemoteFileStream(new HttpClient(new RangeServer(content)), _address, content.Length, _patience);
        byte[] read = new byte[1000];

        // Act & Assert: the end first, then the start and the middle, back and forth, as a zip archive is read
        foreach (int position in new[] { content.Length - 1000, 0, 500_000, 499_000, 65_000 })
        {
            stream.Position = position;
            stream.ReadExactly(read);

            Assert.Equal(content.AsSpan(position, read.Length).ToArray(), read);
        }
    }

    [Fact]
    public void Read_OnAndOn_TakesLargerAndLargerParts()
    {
        // Arrange
        byte[] content = Content(20_000_000);
        var server = new RangeServer(content);
        using var stream = new RemoteFileStream(new HttpClient(server), _address, content.Length, _patience);
        var copy = new MemoryStream();

        // Act
        stream.CopyTo(copy);

        // Assert: from 64 KB up to 8 MB a part, and every byte downloaded once
        Assert.Equal(content, copy.ToArray());
        Assert.Equal(10, server.Requests.Count);
        Assert.Equal(RemoteFileStream.LargestPart, server.Requests.Max(range => range.To - range.From + 1));
        Assert.Equal(content.Length, server.BytesSent);
        Assert.Equal(content.Length, stream.Downloaded);
    }

    [Fact]
    public void Read_AgainWhereItWasRead_DownloadsNothingMore()
    {
        // Arrange
        byte[] content = Content(1_000_000);
        var server = new RangeServer(content);
        using var stream = new RemoteFileStream(new HttpClient(server), _address, content.Length, _patience);
        byte[] read = new byte[100];

        stream.Position = 200_000;
        stream.ReadExactly(read);

        // Act: a little back, as a zip archive reads the header of a file twice
        stream.Position = 200_030;
        stream.ReadExactly(read);

        // Assert
        Assert.Single(server.Requests);
        Assert.Equal(content.AsSpan(200_030, read.Length).ToArray(), read);
    }

    [Fact]
    public void Read_FileSmallerThanAPart_DownloadsItWhole()
    {
        // Arrange
        byte[] content = Content(1000);
        var server = new RangeServer(content);
        using var stream = new RemoteFileStream(new HttpClient(server), _address, content.Length, _patience);
        var copy = new MemoryStream();

        // Act
        stream.CopyTo(copy);

        // Assert
        Assert.Equal(content, copy.ToArray());
        Assert.Equal([(0L, 999L)], server.Requests);
    }

    [Fact]
    public void Read_ServerSendsTheWholeFile_Throws()
    {
        // Arrange
        byte[] content = Content(1_000_000);
        var server = new RangeServer(content) { IgnoresRanges = true };
        using var stream = new RemoteFileStream(new HttpClient(server), _address, content.Length, _patience);

        // Act
        var exc = Assert.Throws<HttpRequestException>(() => stream.ReadByte());

        // Assert
        Assert.Contains("did not give bytes 0-65535 of echooo-test.zip: it answered 200", exc.Message);
    }

    [Fact]
    public void Read_FileOnTheServerHasAnotherSize_Throws()
    {
        // Arrange: the release says the archive is larger than it is
        byte[] content = Content(1_000_000);
        using var stream = new RemoteFileStream(new HttpClient(new RangeServer(content)), _address, content.Length + 1, _patience);

        // Act & Assert
        Assert.Throws<HttpRequestException>(() => stream.ReadByte());
    }

    [Fact]
    public void Read_ServerStopsSending_GivesUp()
    {
        // Arrange: the server sends half of the part and then nothing, keeping the connection open
        byte[] content = Content(1_000_000);
        var server = new RangeServer(content) { Stalls = true };
        using var stream = new RemoteFileStream(new HttpClient(server), _address, content.Length, TimeSpan.FromMilliseconds(300));

        // Act
        var exc = Assert.Throws<TimeoutException>(() => stream.ReadByte());

        // Assert
        Assert.Contains("example.test sent nothing for", exc.Message);
    }

    [Fact]
    public void Read_ServerSendsSlowlyButOn_WaitsForTheWholePart()
    {
        // Arrange: every piece comes well before the server would be given up on, the whole part later than that
        byte[] content = Content(1000);
        var server = new RangeServer(content) { Pause = TimeSpan.FromMilliseconds(250) };
        using var stream = new RemoteFileStream(new HttpClient(server), _address, content.Length, TimeSpan.FromMilliseconds(700));
        var copy = new MemoryStream();

        // Act
        stream.CopyTo(copy);

        // Assert
        Assert.Equal(content, copy.ToArray());
    }

    /// <summary>
    ///     Bytes that differ from place to place, so a byte read from the wrong place shows.
    /// </summary>
    private static byte[] Content(int size)
    {
        byte[] content = new byte[size];
        new Random(1).NextBytes(content);

        return content;
    }
}