using System.Runtime.Versioning;
using Echo.Platform.Windows;
using Echo.Listening;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class WindowsTerminalFocusTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsFocused_SessionReport_TakesPrecedenceOverTheOuterWindow(bool focused)
    {
        var reports = new TerminalFocusReports();
        reports.Report(focused);
        Assert.Equal(focused, new WindowsTerminalFocus(reports).IsFocused());
    }

    [Theory]
    // Windows Terminal: the stand-in console window is owned by the terminal window
    [InlineData(10, 20, 20, true, WindowsTerminalFocus.PseudoConsoleClass, true)]
    [InlineData(10, 20, 30, true, WindowsTerminalFocus.PseudoConsoleClass, false)]
    // The classic console window is its own owner
    [InlineData(10, 10, 10, true, "ConsoleWindowClass", true)]
    [InlineData(10, 10, 30, true, "ConsoleWindowClass", false)]
    // Terminals that do not say which window is theirs: nothing can be told
    [InlineData(10, 10, 30, false, "ConsoleWindowClass", null)]
    [InlineData(10, 10, 30, true, WindowsTerminalFocus.PseudoConsoleClass, null)]
    [InlineData(0, 0, 30, false, "", null)]
    public void Decide_TellsWhetherTheTerminalIsInFrontOrThatItCannotBeTold(int console,
                                                                             int owner,
                                                                             int foreground,
                                                                             bool isConsoleVisible,
                                                                             string consoleClass,
                                                                             bool? expected)
    {
        // Act
        bool? isFocused = WindowsTerminalFocus.Decide(new IntPtr(console), new IntPtr(owner), new IntPtr(foreground), isConsoleVisible, consoleClass);

        // Assert
        Assert.Equal(expected, isFocused);
    }
}