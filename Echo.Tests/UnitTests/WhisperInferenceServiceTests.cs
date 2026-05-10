namespace Echo.Tests.UnitTests;

public sealed class WhisperInferenceServiceTests
{
    [Fact]
    public void IsAudioSilentOrEmpty_NullStream_ReturnsTrue()
    {
        // Act
        bool result = WhisperInferenceService.IsAudioSilentOrEmpty(null);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsAudioSilentOrEmpty_TooShortStream_ReturnsTrue()
    {
        // Arrange: Less than 16000 bytes (0.5 sec)
        using var stream = new MemoryStream(new byte[15000]);

        // Act
        bool result = WhisperInferenceService.IsAudioSilentOrEmpty(stream);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsAudioSilentOrEmpty_SilentAudio_ReturnsTrue()
    {
        // Arrange
        // Array is initialized with zeros (silence)
        byte[] buffer = new byte[20000];
        using var stream = new MemoryStream(buffer);

        // Act
        bool result = WhisperInferenceService.IsAudioSilentOrEmpty(stream);

        // Assert
        Assert.True(result, "Детектор должен признать поток тихим (амплитуда 0 < 500)");
        // Position should be set to 0
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void IsAudioSilentOrEmpty_LoudAudio_ReturnsFalse()
    {
        // Arrange
        byte[] buffer = new byte[20000];

        // Processing skips first 44 bytes (WAV header).
        // Thats why we write sound after 44 index.
        //5000: (0x1388 hex -> 0x88, 0x13 little-endian)
        buffer[50] = 0x88;
        buffer[51] = 0x13;

        using var stream = new MemoryStream(buffer);

        // Act
        bool result = WhisperInferenceService.IsAudioSilentOrEmpty(stream);

        // Assert
        Assert.False(result, "Voice should be detected (amplitude 5000 > 500)");
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void IsAudioSilentOrEmpty_NegativeOverflow_DoesNotCrash()
    {
        // Arrange
        byte[] buffer = new byte[20000];

        // Set -32768 (0x00, 0x80 little-endian)
        buffer[50] = 0x00;
        buffer[51] = 0x80;

        using var stream = new MemoryStream(buffer);

        // Act & Assert
        var exception = Record.Exception(() => WhisperInferenceService.IsAudioSilentOrEmpty(stream));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ProcessAudioAsync_SilentAudio_ReturnsNullWithoutCallingModel()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<WhisperInferenceService>>();
        var assetsMock = new Mock<IAssetsProvider>();

        // Creating options with valid mock settings
        var settings = new Echo.Settings.WhisperSettings { ModelName = "dummy.bin" };
        var optionsMock = Microsoft.Extensions.Options.Options.Create(settings);

        using var service = new WhisperInferenceService(assetsMock.Object, loggerMock.Object, optionsMock);

        // Create an empty valid stream (will be evaluated as silent)
        using var silentStream = new MemoryStream(new byte[20000]);

        // Act
        string? result = await service.ProcessAudioAsync(silentStream);

        // Assert
        Assert.Null(result);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("silent or empty")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}