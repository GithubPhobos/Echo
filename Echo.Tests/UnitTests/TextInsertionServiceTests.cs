using GregsStack.InputSimulatorStandard;
using GregsStack.InputSimulatorStandard.Native;
using TextCopy;

namespace Echo.Tests.UnitTests;

public sealed class TextInsertionServiceTests
{
    private readonly Mock<IClipboard> _clipboardMock = new();
    private readonly Mock<IInputSimulator> _inputMock = new();
    private readonly Mock<IKeyboardSimulator> _keyboardMock = new();

    public TextInsertionServiceTests()
    {
        _inputMock.SetupGet(input => input.Keyboard).Returns(_keyboardMock.Object);
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
        _clipboardMock.Verify(clipboard => clipboard.SetTextAsync("Some text", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InsertTextAsync_AutoInsertEnabled_PressesCtrlV()
    {
        // Arrange
        TextInsertionService service = CreateService(useAutoInsert: true);

        // Act
        await service.InsertTextAsync("Some text");

        // Assert
        _keyboardMock.Verify(keyboard => keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V), Times.Once);
    }

    [Fact]
    public async Task InsertTextAsync_AutoInsertDisabled_PressesNothing()
    {
        // Arrange
        TextInsertionService service = CreateService(useAutoInsert: false);

        // Act
        await service.InsertTextAsync("Some text");

        // Assert
        _keyboardMock.VerifyNoOtherCalls();
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
        _keyboardMock.VerifyNoOtherCalls();
    }

    private TextInsertionService CreateService(bool useAutoInsert) =>
        new(Options.Create(new TextInsertionSettings { UseAutoInsert = useAutoInsert }), _clipboardMock.Object, _inputMock.Object);
}