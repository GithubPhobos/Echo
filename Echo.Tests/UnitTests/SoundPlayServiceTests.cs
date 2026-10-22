using NAudio.Wave;

namespace Echo.Tests.UnitTests;

public sealed class SoundPlayServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    private readonly Mock<ISoundOutput> _output = new();

    public SoundPlayServiceTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void EnsureCanPlaySounds_MissingFiles_LogsWarningAndDoesNotCrash()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<SoundPlayService>>();
        var assetsMock = new Mock<IAssetsProvider>();

        // Mock AssetsProvider to return paths that definitely don't exist
        assetsMock.Setup(a => a.GetFilePath(It.IsAny<string>()))
                  .Returns("C:\\definitely_missing_file.wav");

        var settings = new PushToTalkSettings { PlaySound = true, Volume = 0.5f };
        var optionsMock = Options.Create(settings);

        using var service = new SoundPlayService(Mock.Of<ISoundOutput>(), assetsMock.Object, loggerMock.Object, optionsMock);

        // Act
        // This should not throw an exception, it should catch it internally and disable playback
        var exception = Record.Exception(() => service.EnsureCanPlaySounds());

        // Assert
        Assert.Null(exception);

        loggerMock.Verify(
            s => s.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Sounds: cannot play them")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ReopenSounds_SystemClosedTheLoudspeakers_PlaysThroughNewOnes()
    {
        // Arrange
        var closed = new Mock<IDisposable>();
        _output.SetupSequence(output => output.Play(It.IsAny<ISampleProvider>()))
            .Returns(closed.Object)
            .Returns(Mock.Of<IDisposable>());
        using SoundPlayService service = Service();
        service.EnsureCanPlaySounds();

        // Act
        service.ReopenSounds();

        // Assert
        closed.Verify(output => output.Dispose(), Times.Once);
        _output.Verify(output => output.Play(It.IsAny<ISampleProvider>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ReopenSounds_PressesMadeWhileTheLoudspeakersWereClosed_DoNotPlayAllAtOnce()
    {
        // Arrange
        ISampleProvider? played = null;
        _output.Setup(output => output.Play(It.IsAny<ISampleProvider>()))
            .Callback<ISampleProvider>(source => played = source)
            .Returns(Mock.Of<IDisposable>());
        using SoundPlayService service = Service();
        service.EnsureCanPlaySounds();
        await service.PlayMicrophoneOnSoundAsync();
        await service.PlayMicrophoneOffSoundAsync();

        // Act
        service.ReopenSounds();
        float[] afterReturn = Heard(played!);
        await service.PlayMicrophoneOnSoundAsync();
        float[] afterPress = Heard(played!);

        // Assert
        Assert.All(afterReturn, sample => Assert.Equal(0f, sample));
        Assert.Contains(afterPress, sample => sample != 0f);
    }

    [Fact]
    public void ReopenSounds_LoudspeakersDoNotOpen_KeepsGoingAndTriesAgainNextTime()
    {
        // Arrange
        _output.SetupSequence(output => output.Play(It.IsAny<ISampleProvider>()))
            .Returns(Mock.Of<IDisposable>())
            .Throws(new InvalidOperationException("No loudspeakers."))
            .Returns(Mock.Of<IDisposable>());
        using SoundPlayService service = Service();
        service.EnsureCanPlaySounds();

        // Act
        Exception? exception = Record.Exception(service.ReopenSounds);
        service.ReopenSounds();

        // Assert
        Assert.Null(exception);
        _output.Verify(output => output.Play(It.IsAny<ISampleProvider>()), Times.Exactly(3));
    }

    /// <summary>
    ///     The service with sounds of its own: a tenth of a second of steady tone each.
    /// </summary>
    private SoundPlayService Service()
    {
        string sound = Path.Combine(_directory, "sound.wav");

        using (var writer = new WaveFileWriter(sound, WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)))
            writer.WriteSamples(Enumerable.Repeat(0.5f, 8820).ToArray(), 0, 8820);

        var assets = new Mock<IAssetsProvider>();
        assets.Setup(provider => provider.GetFilePath(It.IsAny<string>())).Returns(sound);
        assets.Setup(provider => provider.CheckIfExists(sound)).Returns(true);

        return new SoundPlayService(
            _output.Object,
            assets.Object,
            Mock.Of<ILogger<SoundPlayService>>(),
            Options.Create(new PushToTalkSettings { PlaySound = true, Volume = 1f }));
    }

    /// <summary>
    ///     What the loudspeakers get next: a hundredth of a second.
    /// </summary>
    private static float[] Heard(ISampleProvider played)
    {
        float[] samples = new float[882];
        played.Read(samples, 0, samples.Length);

        return samples;
    }
}