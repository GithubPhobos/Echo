using System.Globalization;
using Echo.Listening;
using Echo.UI;
using Serilog.Core;
using Serilog.Events;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Echo.Tests.UnitTests;

[Collection(CultureCollection.Name)]
public sealed class ListenScreenTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;

    public ListenScreenTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalCulture;
    }

    [Fact]
    public async Task RunAsync_ArrowsWalkThePanelAndACommandWithValuesOpensTheListOfThem()
    {
        // Arrange: "/" opens the commands; down five times reaches /log; Enter opens its levels,
        // highlighting the current one; up once and Enter picks the more detailed one
        ListenCommands commands = Commands(out ListenActivity activity, out LoggingLevelSwitch levelSwitch);
        (ListenScreen screen, TestConsole console) = Screen(activity, commands);

        console.Input.PushText("/");
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.UpArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("/q");

        // Act
        ListenOutcome outcome = await screen.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(ListenOutcome.Quit, outcome);
        Assert.Equal(LogEventLevel.Verbose, levelSwitch.MinimumLevel);
        Assert.Contains(activity.Log.Snapshot(), entry => entry.Kind == EntryKind.Command && entry.Text == "/log verbose");
    }

    [Fact]
    public async Task RunAsync_ArrowsWithoutAPanel_ScrollTheLog()
    {
        // Arrange: more lines than fit, then up three rows, down one
        ListenCommands commands = Commands(out ListenActivity activity, out _);

        for (int index = 1; index <= 80; index++)
            activity.Log.Add(EntryKind.Message, $"line {index}");

        (ListenScreen screen, TestConsole console) = Screen(activity, commands);

        console.Input.PushKey(ConsoleKey.UpArrow);
        console.Input.PushKey(ConsoleKey.UpArrow);
        console.Input.PushKey(ConsoleKey.UpArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushTextWithEnter("/quit");

        // Act
        await screen.RunAsync(CancellationToken.None);

        // Assert: what was drawn ended two lines above the end, and said how to get back down
        Assert.Contains("line 78", console.Output);
        Assert.Contains("End to the very bottom", console.Output);
    }

    [Fact]
    public void ArrowRows_ArrowsThatKeepComing_ScrollAlittleFasterThanARowEach()
    {
        // Arrange
        ListenCommands commands = Commands(out ListenActivity activity, out _);
        (ListenScreen screen, _) = Screen(activity, commands);

        // Act: one press, then twenty in a row the other way, as the mouse wheel sends them
        int single = screen.ArrowRows(1);
        int rows = Enumerable.Range(0, 20).Sum(_ => screen.ArrowRows(-1));

        // Assert
        Assert.Equal(1, single);
        Assert.InRange(rows, 28, 30);
    }

    [Fact]
    public async Task RunAsync_CommandsTypedAndQuit_RunsThemAndGivesTheTerminalBack()
    {
        // Arrange
        ListenCommands commands = Commands(out ListenActivity activity, out _);
        activity.SetState(ListenState.Listening);

        (ListenScreen screen, TestConsole console) = Screen(activity, commands);

        console.Input.PushTextWithEnter("/pause");
        console.Input.PushTextWithEnter("/quit");

        // Act
        ListenOutcome outcome = await screen.RunAsync(CancellationToken.None);

        // Assert
        Assert.Equal(ListenOutcome.Quit, outcome);
        Assert.True(activity.IsPaused);
    }

    [Fact]
    public void Draw_ScrolledBackWhileTheOldestLinesLeaveTheLog_NothingOnTheScreenMoves()
    {
        // Arrange: a full log, scrolled back by a page
        var log = new ScreenLog(capacity: 40);
        var activity = new ListenActivity(log, () => false);

        for (int line = 1; line <= 40; line++)
            log.Add(EntryKind.Message, $"line {line}");

        var commands = new ListenCommands(activity, new LoggingLevelSwitch(LogEventLevel.Debug), Mock.Of<IUserSettingsStore>(store => store.Load() == new UserSettings()), () => Mock.Of<ITextInsertionService>(), () => []);
        (ListenScreen screen, TestConsole console) = Screen(activity, commands);

        screen.Draw();
        screen.Handle(new ConsoleKeyInfo('\0', ConsoleKey.PageUp, shift: false, alt: false, control: false));
        screen.Draw();

        int drawn = console.Output.Length;

        // Act: new lines push the oldest ones out of the log
        for (int line = 41; line <= 50; line++)
            log.Add(EntryKind.Message, $"line {line}");

        screen.Draw();

        // Assert: not a single row was written again
        Assert.Equal(drawn, console.Output.Length);

        // Back at the bottom, the new lines are there
        screen.Handle(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        screen.Draw();

        Assert.Contains("line 50", console.Output[drawn..]);
    }

    [Fact]
    public void Handle_TypingWhileEchoStarts_TakesNothingUntilItIsReady()
    {
        // Arrange: the model is still loading
        ListenCommands commands = Commands(out ListenActivity activity, out _, isStarted: false);
        (ListenScreen screen, _) = Screen(activity, commands);

        // Act
        ListenOutcome whileStarting = Type(screen, "/quit");

        activity.SetState(ListenState.Listening);

        ListenOutcome whenReady = Type(screen, "/quit");

        // Assert: nothing typed early is left over to spoil the command typed later
        Assert.Equal(ListenOutcome.Continue, whileStarting);
        Assert.Equal(ListenOutcome.Quit, whenReady);
    }

    [Fact]
    public void Handle_EchoCouldNotStart_TakesCommands()
    {
        // Arrange: /setup has to be typed to put things right
        ListenCommands commands = Commands(out ListenActivity activity, out _, isStarted: false);
        (ListenScreen screen, _) = Screen(activity, commands);

        activity.SetState(ListenState.Failed);

        // Act
        ListenOutcome outcome = Type(screen, "/quit");

        // Assert
        Assert.Equal(ListenOutcome.Quit, outcome);
    }

    [Fact]
    public void Handle_LogAndCtrlCWhileEchoStarts_StillWork()
    {
        // Arrange
        ListenCommands commands = Commands(out ListenActivity activity, out _, isStarted: false);

        for (int index = 1; index <= 80; index++)
            activity.Log.Add(EntryKind.Message, $"line {index}");

        (ListenScreen screen, TestConsole console) = Screen(activity, commands);

        // Act
        screen.Handle(new ConsoleKeyInfo('\0', ConsoleKey.PageUp, shift: false, alt: false, control: false));
        screen.Draw();

        ListenOutcome outcome = screen.Handle(new ConsoleKeyInfo('\u0003', ConsoleKey.C, shift: false, alt: false, control: true));

        // Assert
        Assert.Contains("End to the very bottom", console.Output);
        Assert.Equal(ListenOutcome.Quit, outcome);
    }

    [Theory]
    [InlineData(ConsoleKey.Backspace, '\b')]
    [InlineData(ConsoleKey.W, '\u0017')]
    public void Handle_CtrlBackspaceOrCtrlW_TakesAwayTheLastWord(ConsoleKey key, char character)
    {
        // Arrange: Linux terminals send Ctrl+Backspace as ^H, which .NET reads as Backspace with Ctrl
        ListenCommands commands = Commands(out ListenActivity activity, out _);
        (ListenScreen screen, TestConsole console) = Screen(activity, commands);

        foreach (char typed in "/log debug")
            screen.Handle(new ConsoleKeyInfo(typed, ConsoleKey.NoName, shift: false, alt: false, control: false));

        // Act
        screen.Handle(new ConsoleKeyInfo(character, key, shift: false, alt: false, control: true));
        screen.Draw();

        // Assert
        Assert.Contains("> /log █", console.Output);
        Assert.DoesNotContain("debug", console.Output);
    }

    /// <param name="isStarted">Whether Echo is ready, so that the command line takes what is typed.</param>
    private static ListenCommands Commands(out ListenActivity activity, out LoggingLevelSwitch levelSwitch, bool isStarted = true)
    {
        activity = new ListenActivity(new ScreenLog(), () => false);
        levelSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);

        if (isStarted)
            activity.SetState(ListenState.Listening);

        return new ListenCommands(activity, levelSwitch, Mock.Of<IUserSettingsStore>(store => store.Load() == new UserSettings()), () => Mock.Of<ITextInsertionService>(), () => [("Model", "ggml-tiny.bin"), ("Microphone", "Desk microphone")]);
    }

    /// <summary>
    ///     Types the text into the command line and presses Enter, and gives what that led to.
    /// </summary>
    private static ListenOutcome Type(ListenScreen screen, string text)
    {
        foreach (char typed in text)
            screen.Handle(new ConsoleKeyInfo(typed, ConsoleKey.NoName, shift: false, alt: false, control: false));

        return screen.Handle(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));
    }

    private static (ListenScreen Screen, TestConsole Console) Screen(ListenActivity activity, ListenCommands commands)
    {
        TestConsole console = new TestConsole().Width(100).Height(30).Interactive();

        var screen = new ListenScreen(console, Theme.Full, activity, commands, new ListenInfo("tiny", "`"), TimeSpan.FromMilliseconds(1));

        return (screen, console);
    }
}