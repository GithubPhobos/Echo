using Echo.Listening;

namespace Echo.Tests.UnitTests;

public sealed class TerminalFocusReportsTests
{
    private readonly ManualClock _clock = new();

    [Fact]
    public void Handle_FirstAnswerOfTheTerminal_IsTakenAsTheStateOfItsWindow()
    {
        // Arrange
        var reports = new TerminalFocusReports(_clock);
        reports.Started();

        // Act: the terminal of GNOME answers at once, here with "active"
        bool isReport = reports.Handle(PageUp);

        // Assert
        Assert.True(isReport);
        Assert.True(reports.IsFocused);
    }

    [Fact]
    public void Handle_TerminalThatAnswered_TellsWhenItsWindowStopsAndStartsBeingActive()
    {
        // Arrange
        var reports = new TerminalFocusReports(_clock);
        reports.Started();
        reports.Handle(PageUp);

        _clock.Advance(TimeSpan.FromMinutes(1));

        // Act & Assert: F3 is "not active"; the next Page Up is "active again"
        Assert.True(reports.Handle(F3));
        Assert.False(reports.IsFocused);

        Assert.True(reports.Handle(PageUp));
        Assert.True(reports.IsFocused);
    }

    [Fact]
    public void Handle_PageUpWhileTheWindowIsActive_IsTheUsersOwnKey()
    {
        // Arrange
        var reports = new TerminalFocusReports(_clock);
        reports.Started();
        reports.Handle(PageUp);

        _clock.Advance(TimeSpan.FromSeconds(5));

        // Act & Assert: it scrolls the log
        Assert.False(reports.Handle(PageUp));
        Assert.True(reports.IsFocused);
    }

    [Fact]
    public void Handle_TerminalThatDidNotAnswerAtOnce_IsNeverBelieved()
    {
        // Arrange: a terminal without the reports, and a user who presses Page Up and F3 later on
        var reports = new TerminalFocusReports(_clock);
        reports.Started();

        _clock.Advance(TimeSpan.FromSeconds(2));

        // Act & Assert: both are the user's keys, and the state stays unknown
        Assert.False(reports.Handle(PageUp));
        Assert.False(reports.Handle(F3));
        Assert.Null(reports.IsFocused);
    }

    [Fact]
    public void Handle_PageUpOfAPersonQuickAfterTheScreenAppears_IsTheUsersKey()
    {
        // Arrange: a terminal without the reports; even a quick person needs longer than the terminal's answer
        var reports = new TerminalFocusReports(_clock);
        reports.Started();

        _clock.Advance(TimeSpan.FromMilliseconds(200));

        // Act & Assert: it scrolls the log and leaves the state unknown, so the text is still typed elsewhere
        Assert.False(reports.Handle(PageUp));
        Assert.Null(reports.IsFocused);
    }

    [Fact]
    public void Handle_KeysThatAreNoReports_AreLeftAlone()
    {
        // Arrange
        var reports = new TerminalFocusReports(_clock);
        reports.Started();

        // Act & Assert
        Assert.False(reports.Handle(new ConsoleKeyInfo('a', ConsoleKey.A, shift: false, alt: false, control: false)));
        Assert.False(reports.Handle(new ConsoleKeyInfo('\0', ConsoleKey.PageUp, shift: true, alt: false, control: false)));
        Assert.Null(reports.IsFocused);
    }

    [Fact]
    public void Stopped_ReportsNoLongerAskedFor_ForgetsTheState()
    {
        // Arrange
        var reports = new TerminalFocusReports(_clock);
        reports.Started();
        reports.Handle(PageUp);

        // Act
        reports.Stopped();

        // Assert
        Assert.Null(reports.IsFocused);
    }

    private static ConsoleKeyInfo PageUp => new('\0', ConsoleKey.PageUp, shift: false, alt: false, control: false);

    private static ConsoleKeyInfo F3 => new('\0', ConsoleKey.F3, shift: false, alt: false, control: false);

    /// <summary>
    ///     A clock that moves only when told to.
    /// </summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan time) => _ticks += time.Ticks;
    }
}