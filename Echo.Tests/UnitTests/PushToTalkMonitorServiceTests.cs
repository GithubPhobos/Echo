namespace Echo.Tests.UnitTests;

public sealed class PushToTalkMonitorServiceTests
{
    private readonly Mock<ILogger<PushToTalkMonitorService>> _loggerMock = new();
    private readonly Mock<ISoundPlayService> _soundMock = new();

    [Fact]
    public void InitializePushToTalkHotkeysOrThrow_InvalidMainKey_ThrowsException()
    {
        // Arrange
        var badSettings = new PushToTalkSettings { Key = "INVALID_KEY_123" };
        var optionsMock = Options.Create(badSettings);

        using var service = new PushToTalkMonitorService(_soundMock.Object, _loggerMock.Object, optionsMock);

        // Act & Assert
        var ex = Assert.Throws<Exception>(() => service.InitializePushToTalkHotkeysOrThrow());

        Assert.Contains("Couldn't parse push to talk key", ex.Message);
    }

    [Fact]
    public void InitializePushToTalkHotkeysOrThrow_ValidKey_DoesNotThrow()
    {
        // Arrange
        var goodSettings = new PushToTalkSettings { Key = "`", Modifier = "alt" };
        var optionsMock = Options.Create(goodSettings);

        using var service = new PushToTalkMonitorService(_soundMock.Object, _loggerMock.Object, optionsMock);

        // Act & Assert
        var exception = Record.Exception(() => service.InitializePushToTalkHotkeysOrThrow());

        Assert.Null(exception);
    }
}