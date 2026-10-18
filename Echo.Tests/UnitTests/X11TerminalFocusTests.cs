using System.Runtime.Versioning;
using Echo.Listening;
using Echo.Platform.Linux;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class X11TerminalFocusTests
{
    /// <summary>
    ///     A window tree: the terminal names its inner window 30, inside window 20, inside the frame 10 the desktop
    ///     made for it.
    /// </summary>
    private static readonly Dictionary<ulong, ulong?> _parents = new() { [30] = 20, [20] = 10, [10] = null, [99] = null };

    [Theory]
    [InlineData(30ul, true)]
    [InlineData(20ul, true)]
    [InlineData(10ul, true)]
    [InlineData(99ul, false)]
    public void Decide_ActiveWindowHoldsTheTerminalsWindow_IsFocused(ulong active, bool expected)
    {
        // Act & Assert
        Assert.Equal(expected, X11TerminalFocus.Decide(30, active, window => _parents.GetValueOrDefault(window)));
    }

    [Fact]
    public void IsFocused_TerminalThatNamesNoWindowAndReportsNothing_CannotTell()
    {
        // Arrange: a terminal that sets no WINDOWID and sends no reports about its window
        var focus = new X11TerminalFocus(_ => null, new TerminalFocusReports(TimeProvider.System));

        // Act & Assert
        Assert.Null(focus.IsFocused());
    }

    [Fact]
    public void IsFocused_TerminalThatNamesNoWindowButReportsIt_TakesTheReport()
    {
        // Arrange: GNOME Terminal sets no WINDOWID, but answers the request for reports with "active"
        var reports = new TerminalFocusReports(TimeProvider.System);
        reports.Started();
        reports.Handle(new ConsoleKeyInfo('\0', ConsoleKey.PageUp, shift: false, alt: false, control: false));

        var focus = new X11TerminalFocus(_ => null, reports);

        // Act & Assert
        Assert.True(focus.IsFocused());
    }
}