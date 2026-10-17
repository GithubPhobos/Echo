namespace Echo.Tests.UnitTests;

public sealed class TerminalFocusTests
{
    [Theory]
    // Windows Terminal: the stand-in console window is owned by the terminal window
    [InlineData(10, 20, 20, true, TerminalFocus.PseudoConsoleClass, true)]
    [InlineData(10, 20, 30, true, TerminalFocus.PseudoConsoleClass, false)]
    // The classic console window is its own owner
    [InlineData(10, 10, 10, true, "ConsoleWindowClass", true)]
    [InlineData(10, 10, 30, true, "ConsoleWindowClass", false)]
    // Terminals that do not say which window is theirs: nothing can be told
    [InlineData(10, 10, 30, false, "ConsoleWindowClass", null)]
    [InlineData(10, 10, 30, true, TerminalFocus.PseudoConsoleClass, null)]
    [InlineData(0, 0, 30, false, "", null)]
    public void Decide_TellsWhetherTheTerminalIsInFrontOrThatItCannotBeTold(int console,
                                                                             int owner,
                                                                             int foreground,
                                                                             bool isConsoleVisible,
                                                                             string consoleClass,
                                                                             bool? expected)
    {
        // Act
        bool? isFocused = TerminalFocus.Decide(new IntPtr(console), new IntPtr(owner), new IntPtr(foreground), isConsoleVisible, consoleClass);

        // Assert
        Assert.Equal(expected, isFocused);
    }
}