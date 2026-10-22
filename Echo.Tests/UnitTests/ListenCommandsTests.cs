using System.Globalization;
using Echo.Extensions;
using Echo.Listening;
using Echo.Setup;
using Echo.UI;
using Serilog.Core;
using Serilog.Events;
using Spectre.Console;

namespace Echo.Tests.UnitTests;

[Collection(CultureCollection.Name)]
public sealed class ListenCommandsTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;

    public ListenCommandsTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalCulture;
    }

    [Fact]
    public void Run_AnswerInTheLogAndChangeWhatTheyAreAbout()
    {
        // Arrange
        ListenCommands commands = Commands(out ListenActivity activity, out LoggingLevelSwitch levelSwitch);

        activity.SetState(ListenState.Listening);

        // Act & Assert
        Assert.Equal(ListenOutcome.Continue, commands.Run("/pause"));
        Assert.True(activity.IsPaused);
        Assert.Equal(ListenState.Paused, activity.State);

        Assert.Equal(ListenOutcome.Continue, commands.Run("/resume"));
        Assert.Equal(ListenState.Listening, activity.State);

        commands.Run("/log warn");
        Assert.Equal(LogEventLevel.Warning, levelSwitch.MinimumLevel);

        commands.Run("/log loud");
        Assert.Equal(LogEventLevel.Warning, levelSwitch.MinimumLevel);

        commands.Run("/nothing");
        commands.Run("hello");
        commands.Run("/info");

        Assert.Equal(ListenOutcome.Setup, commands.Run("/setup"));
        Assert.Equal(ListenOutcome.Quit, commands.Run("/QUIT"));

        Assert.Equal(
            [
                "/pause", "Echo no longer listens to the key. /resume to continue.",
                "/resume", "Echo listens to the key again.",
                "/log warn", "Log detail: warning.",
                "/log loud", "No such level: loud. Levels: verbose, debug, information, warning, error, fatal.",
                "/nothing", "No such command: /nothing. /help lists them.",
                "hello", "Commands start with /. /help lists them.",
                "/info", "Settings:", "  Model        ggml-tiny.bin", "  Microphone   Desk microphone",
                "/setup", "/QUIT",
            ],
            activity.Log.Snapshot().Select(entry => entry.Text));

        // The log detail is chosen from a list that marks the one in effect
        IReadOnlyList<PanelRow> levels = commands.Options(commands.All.Single(command => command.Name == ListenCommands.LogLevel));

        Assert.Equal("warning", levels.Single(row => row.IsCurrent).Label);
        Assert.Equal("/log debug", levels[1].Text);
    }

    [Fact]
    public void Run_LogDetailChanged_IsSavedAndUsedAtTheNextStart()
    {
        // Arrange: a settings file the user already has, with a setting of its own
        string directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var paths = new AppPaths(directory, directory);
            var store = new UserSettingsStore(paths);

            store.Save(new UserSettings { SetupVersion = 1, ModelName = "ggml-small.bin" });

            var activity = new ListenActivity(new ScreenLog(), () => false);
            var commands = new ListenCommands(activity, new LoggingLevelSwitch(LogEventLevel.Debug), store, () => Mock.Of<ITextInsertionService>(), () => []);

            // Act
            commands.Run("/log information");

            // Assert: kept in the settings like any other, and read back as the log detail of the next start
            UserSettings saved = store.Load();

            Assert.Equal("Information", saved.Additional.Get(AdditionalSettings.LogLevel));
            Assert.Equal("ggml-small.bin", saved.ModelName);
            Assert.Equal(LogEventLevel.Information, ServiceCollectionExtensions.LogLevelOf(ServiceCollectionExtensions.BuildConfiguration(paths)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Run_LogDetail_NamesEveryLevelInItsOwnColor()
    {
        // Arrange
        ListenCommands commands = Commands(out ListenActivity activity, out _);

        // Act
        commands.Run("/log warning");
        commands.Run("/log");

        // Assert: the level set is drawn in the color of warnings
        IReadOnlyList<ScreenEntry> log = activity.Log.Snapshot();
        ScreenEntry changed = log[1];

        Assert.Equal([(changed.Text.IndexOf("warning", StringComparison.Ordinal), "warning".Length, Theme.Warning)], changed.Accents);

        // The answer that lists them all shows every level in its color, and no two levels share one
        ScreenEntry listed = log[3];

        Assert.Equal(
            ["warning", "verbose", "debug", "information", "warning", "error", "fatal"],
            listed.Accents.Select(accent => listed.Text.Substring(accent.Start, accent.Length)));

        Assert.Equal(6, listed.Accents.Select(accent => accent.Style).Distinct().Count());

        // The list the level is picked from has the same colors
        IReadOnlyList<PanelRow> levels = commands.Options(commands.All.Single(command => command.Name == ListenCommands.LogLevel));

        Assert.Equal(ListenCommands.Levels.Select(ListenFrame.LevelStyle), levels.Select(row => row.LabelStyle));
    }

    [Fact]
    public void Run_Copy_PutsTheLastRecognizedPhraseIntoTheClipboard()
    {
        // Arrange: two phrases said, and a line of the log after them
        var insertion = new Mock<ITextInsertionService>();
        insertion.Setup(i => i.CopyAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        ListenCommands commands = Commands(insertion.Object, out ListenActivity activity);

        activity.Log.Add(EntryKind.Dictated, "first phrase", "0.40 s");
        activity.Log.Add(EntryKind.Dictated, "second phrase", "0.38 s");
        activity.Log.Add(EntryKind.Message, "Recording...");

        // Act
        commands.Run("/copy");

        // Assert
        insertion.Verify(i => i.CopyAsync("second phrase"), Times.Once);
        Assert.Equal((EntryKind.Answer, "The last recognized text is in the clipboard."), (activity.Log.Snapshot()[^1].Kind, activity.Log.Snapshot()[^1].Text));
    }

    [Fact]
    public void Run_CopyBeforeAnyPhrase_SaysThereIsNothingToCopy()
    {
        // Arrange
        var insertion = new Mock<ITextInsertionService>();
        ListenCommands commands = Commands(insertion.Object, out ListenActivity activity);

        // Act
        commands.Run("/copy");

        // Assert
        insertion.Verify(i => i.CopyAsync(It.IsAny<string>()), Times.Never);
        Assert.Equal("Nothing to copy yet: no phrase has been recognized.", activity.Log.Snapshot()[^1].Text);
    }

    [Fact]
    public void Run_CopyClipboardFails_SaysWhyAsAWarning()
    {
        // Arrange
        var insertion = new Mock<ITextInsertionService>();
        insertion.Setup(i => i.CopyAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("xsel is missing"));

        ListenCommands commands = Commands(insertion.Object, out ListenActivity activity);
        activity.Log.Add(EntryKind.Dictated, "hello", "0.40 s");

        // Act
        commands.Run("/copy");

        // Assert
        ScreenEntry answer = activity.Log.Snapshot()[^1];

        Assert.Equal((EntryKind.Warning, "The text could not be copied: xsel is missing"), (answer.Kind, answer.Text));
    }

    [Fact]
    public async Task Run_CopiesStillUnderWayWhenTheScreenIsLeft_AreWaitedForEveryOne()
    {
        // Arrange: the clipboard takes its time with both copies
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var insertion = new Mock<ITextInsertionService>();
        insertion.SetupSequence(i => i.CopyAsync(It.IsAny<string>())).Returns(first.Task).Returns(second.Task);

        ListenCommands commands = Commands(insertion.Object, out ListenActivity activity);
        activity.Log.Add(EntryKind.Dictated, "hello", "0.40 s");

        // Act
        commands.Run("/copy");
        commands.Run("/copy");
        ListenOutcome outcome = commands.Run("/quit");

        second.SetResult();
        bool isDoneBeforeTheFirst = commands.Copying.IsCompleted;
        first.SetResult();

        // Assert: leaving the screen waits for every copy, so none changes the clipboard behind the next screen
        Assert.Equal(ListenOutcome.Quit, outcome);
        Assert.False(isDoneBeforeTheFirst);
        await commands.Copying.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static ListenCommands Commands(out ListenActivity activity, out LoggingLevelSwitch levelSwitch)
    {
        activity = new ListenActivity(new ScreenLog(), () => false);
        levelSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);

        return new ListenCommands(activity, levelSwitch, Mock.Of<IUserSettingsStore>(store => store.Load() == new UserSettings()), () => Mock.Of<ITextInsertionService>(), () => [("Model", "ggml-tiny.bin"), ("Microphone", "Desk microphone")]);
    }

    private static ListenCommands Commands(ITextInsertionService insertion, out ListenActivity activity)
    {
        activity = new ListenActivity(new ScreenLog(), () => false);

        return new ListenCommands(activity, new LoggingLevelSwitch(LogEventLevel.Debug), Mock.Of<IUserSettingsStore>(), () => insertion, () => []);
    }
}