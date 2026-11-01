namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class PulseSoundOutputTests
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(5);

    private readonly StalledPulseServer _server = new();

    private readonly ISampleProvider _source =
        Mock.Of<ISampleProvider>(source => source.WaveFormat == WaveFormat.CreateIeeeFloatWaveFormat(16000, 1));

    [Fact]
    public void Dispose_ServerStoppedAnswering_ReturnsAndTheStreamIsFreedOnceTheServerAnswers()
    {
        // Arrange: the server holds the first write
        IDisposable playback = new PulseSoundOutput(_server, TimeSpan.FromMilliseconds(200)).Play(_source);
        Assert.True(_server.Entered.Wait(_patience));

        // Act
        var closing = Stopwatch.StartNew();
        playback.Dispose();
        closing.Stop();

        // Assert: closing waited only its own time and freed nothing under the write; the stream goes once it returns
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
        IDisposable playback = new PulseSoundOutput(_server).Play(_source);
        Assert.True(_server.Entered.Wait(_patience));

        // Act
        playback.Dispose();

        // Assert
        Assert.Equal(1, _server.Freed);
    }
}