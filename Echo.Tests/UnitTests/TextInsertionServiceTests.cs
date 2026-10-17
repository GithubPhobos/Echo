namespace Echo.Tests.UnitTests;

public sealed class TextInsertionServiceTests
{
    private readonly Mock<ITextClipboard> _clipboardMock = new();
    private readonly Mock<IPasteKeys> _pasteMock = new();
    private readonly Mock<ILogger<TextInsertionService>> _loggerMock = new();

    public TextInsertionServiceTests()
    {
        _pasteMock.Setup(paste => paste.PasteAsync()).ReturnsAsync(true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InsertTextAsync_AnyText_IsLeftInTheClipboard(bool useAutoInsert)
    {
        // Arrange
        TextInsertionService service = CreateService(useAutoInsert);

        // Act
        await service.InsertTextAsync("Some text");

        // Assert
        _clipboardMock.Verify(clipboard => clipboard.SetTextAsync("Some text"), Times.Once);
    }

    [Fact]
    public async Task InsertTextAsync_AutoInsertEnabled_PressesThePasteKeys()
    {
        // Arrange
        TextInsertionService service = CreateService(useAutoInsert: true);

        // Act
        await service.InsertTextAsync("Some text");

        // Assert
        _pasteMock.Verify(paste => paste.PasteAsync(), Times.Once);
    }

    [Fact]
    public async Task InsertTextAsync_AutoInsertDisabled_PressesNothing()
    {
        // Arrange
        TextInsertionService service = CreateService(useAutoInsert: false);

        // Act
        await service.InsertTextAsync("Some text");

        // Assert
        _pasteMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InsertTextAsync_SystemDoesNotLetEchoPressKeys_SaysThatTheTextIsCopied()
    {
        // Arrange
        _pasteMock.Setup(paste => paste.PasteAsync()).ReturnsAsync(false);

        TextInsertionService service = CreateService(useAutoInsert: true);

        // Act
        await service.InsertTextAsync("Some text");

        // Assert
        _clipboardMock.Verify(clipboard => clipboard.SetTextAsync("Some text"), Times.Once);
        _loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("paste it yourself")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task InsertTextAsync_EmptyOrNullText_ReturnsImmediately(string? invalidText)
    {
        // Arrange
        TextInsertionService service = CreateService(useAutoInsert: true);

        // Act
        await service.InsertTextAsync(invalidText);

        // Assert
        _clipboardMock.VerifyNoOtherCalls();
        _pasteMock.VerifyNoOtherCalls();
    }

    private TextInsertionService CreateService(bool useAutoInsert) =>
        new(Options.Create(new TextInsertionSettings { UseAutoInsert = useAutoInsert }),
            _clipboardMock.Object,
            _pasteMock.Object,
            _loggerMock.Object);
}