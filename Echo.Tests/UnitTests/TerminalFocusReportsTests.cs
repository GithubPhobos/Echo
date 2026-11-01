using Echo.Listening;

namespace Echo.Tests.UnitTests;

public sealed class TerminalFocusReportsTests
{
    [Fact]
    public void Started_WithoutAnInitialEvent_RemainsUnknown()
    {
        var reports = new TerminalFocusReports();
        reports.Started();
        Assert.Null(reports.IsFocused);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Report_ActualEvent_UpdatesTheSession(bool focused)
    {
        var reports = new TerminalFocusReports();
        reports.Started();
        reports.Report(focused);
        Assert.Equal(focused, reports.IsFocused);
        reports.Report(!focused);
        Assert.Equal(!focused, reports.IsFocused);
    }

    [Fact]
    public void Started_AnotherScreen_ForgetsTheOldFocus()
    {
        var reports = new TerminalFocusReports();
        reports.Report(true);
        reports.Stopped();
        Assert.Null(reports.IsFocused);
        reports.Report(false);
        reports.Started();
        Assert.Null(reports.IsFocused);
    }
}