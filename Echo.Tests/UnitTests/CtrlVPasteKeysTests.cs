using System.Runtime.Versioning;
using Echo.Platform.Windows;
using GregsStack.InputSimulatorStandard;
using GregsStack.InputSimulatorStandard.Native;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class CtrlVPasteKeysTests
{
    [Fact]
    public async Task PasteAsync_PressesCtrlV()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Arrange
        var keyboardMock = new Mock<IKeyboardSimulator>();
        var inputMock = new Mock<IInputSimulator>();

        inputMock.SetupGet(input => input.Keyboard).Returns(keyboardMock.Object);

        var pasteKeys = new CtrlVPasteKeys(inputMock.Object);

        // Act
        bool isPasted = await pasteKeys.PasteAsync();

        // Assert
        Assert.True(isPasted);
        keyboardMock.Verify(keyboard => keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V), Times.Once);
    }
}