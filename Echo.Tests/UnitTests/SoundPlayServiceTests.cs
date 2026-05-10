namespace Echo.Tests.UnitTests;

public sealed class SoundPlayServiceTests
{
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

        using var service = new SoundPlayService(assetsMock.Object, loggerMock.Object, optionsMock);

        // Act
        // This should not throw an exception, it should catch it internally and disable playback
        var exception = Record.Exception(() => service.EnsureCanPlaySounds());

        // Assert
        Assert.Null(exception);

        loggerMock.Verify(
            s => s.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Cannot play sounds")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}