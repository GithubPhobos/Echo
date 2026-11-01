namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class PulseAudioInputStreamTests
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(5);

    private readonly StalledPulseServer _server = new();

    [Fact]
    public void Dispose_ServerStoppedAnswering_ReturnsAndTheStreamIsFreedOnceTheServerAnswers()
    {
        // Arrange: the server holds the first read
        var microphone = new PulseAudioInputStream(null, 10, _server, TimeSpan.FromMilliseconds(200));
        microphone.Start();
        Assert.True(_server.Entered.Wait(_patience));

        // Act
        var closing = Stopwatch.StartNew();
        microphone.Dispose();
        closing.Stop();

        // Assert: closing waited only its own time and freed nothing under the read; the stream goes once it returns
        Assert.True(closing.Elapsed < _patience);
        Assert.Equal(0, _server.Freed);

        _server.Answering.Set();
        Assert.True(SpinWait.SpinUntil(() => _server.Freed == 1, _patience));
    }

    [Fact]
    public void Dispose_ServerAnswering_FreesTheStreamOnce()
    {
        // Arrange
        _server.Answering.Set();
        var microphone = new PulseAudioInputStream(null, 10, _server);
        microphone.Start();
        Assert.True(_server.Entered.Wait(_patience));

        // Act
        microphone.Dispose();

        // Assert
        Assert.Equal(1, _server.Freed);
    }

    [Fact]
    public void Dispose_NeverStarted_OpensAndFreesNothing()
    {
        // Act
        new PulseAudioInputStream(null, 10, _server).Dispose();

        // Assert
        Assert.Equal(0, _server.Opened);
        Assert.Equal(0, _server.Freed);
    }
}