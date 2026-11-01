namespace Echo.Tests.UnitTests;

public sealed class TextInsertionServiceTests
{
    /// <summary>
    ///     How long a test waits for what it holds up.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

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

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task PrepareAsync_GetsThePasteKeysReadyOnlyWhenTheyWillBePressed(bool useAutoInsert, int expected)
    {
        // Arrange
        TextInsertionService service = CreateService(useAutoInsert);

        // Act
        await service.PrepareAsync(CancellationToken.None);

        // Assert
        _pasteMock.Verify(paste => paste.PrepareAsync(CancellationToken.None), Times.Exactly(expected));
    }

    [Fact]
    public async Task CopyAsync_WhileAPhraseIsBeingPasted_WaitsUntilItIsPasted()
    {
        // Arrange: the phrase is in the clipboard, and its keys are not pressed yet
        var phraseIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var phraseDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? inClipboard = null;
        string? pasted = null;

        _clipboardMock.Setup(clipboard => clipboard.SetTextAsync(It.IsAny<string>())).Returns(async (string text) =>
        {
            inClipboard = text;

            if (text == "current phrase")
            {
                phraseIn.TrySetResult();
                await phraseDone.Task;
            }
        });

        _pasteMock.Setup(paste => paste.PasteAsync()).Returns(() =>
        {
            pasted = inClipboard;

            return Task.FromResult(true);
        });

        TextInsertionService service = CreateService(useAutoInsert: true);

        Task inserting = service.InsertTextAsync("current phrase");
        await phraseIn.Task.WaitAsync(_patience);

        // Act: the user copies an earlier phrase just then
        Task copying = service.CopyAsync("previous phrase");
        phraseDone.SetResult();

        await inserting.WaitAsync(_patience);
        await copying.WaitAsync(_patience);

        // Assert: the phrase is pasted whole, and the clipboard holds what the user copied
        Assert.Equal("current phrase", pasted);
        Assert.Equal("previous phrase", inClipboard);
    }

    [Fact]
    public async Task InsertTextAsync_WhileACopyIsUnderWay_PastesItsOwnPhrase()
    {
        // Arrange: copying an earlier phrase takes its time
        var copyIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var copyDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? inClipboard = null;
        string? pasted = null;

        _clipboardMock.Setup(clipboard => clipboard.SetTextAsync(It.IsAny<string>())).Returns(async (string text) =>
        {
            if (text == "previous phrase")
            {
                copyIn.TrySetResult();
                await copyDone.Task;
            }

            inClipboard = text;
        });

        _pasteMock.Setup(paste => paste.PasteAsync()).Returns(() =>
        {
            pasted = inClipboard;

            return Task.FromResult(true);
        });

        TextInsertionService service = CreateService(useAutoInsert: true);

        Task copying = service.CopyAsync("previous phrase");
        await copyIn.Task.WaitAsync(_patience);

        // Act: a phrase is recognized just then
        Task inserting = service.InsertTextAsync("current phrase");
        copyDone.SetResult();

        await copying.WaitAsync(_patience);
        await inserting.WaitAsync(_patience);

        // Assert: the late copy does not take the place of the phrase between its copying and its pasting
        Assert.Equal("current phrase", pasted);
    }

    private TextInsertionService CreateService(bool useAutoInsert) =>
        new(Options.Create(new TextInsertionSettings { UseAutoInsert = useAutoInsert }),
            _clipboardMock.Object,
            _pasteMock.Object,
            _loggerMock.Object);
}