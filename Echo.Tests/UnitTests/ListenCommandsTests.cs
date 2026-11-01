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

    [Theory]
    [InlineData(nameof(UpdateStart.Started), nameof(UpdateStage.Downloading), "Downloading Echo 2.0.2: the top line shows how far it has got")]
    [InlineData(nameof(UpdateStart.Checking), nameof(UpdateStage.Unknown), "Checking for a new version…")]
    [InlineData(nameof(UpdateStart.AlreadyDownloading), nameof(UpdateStage.Downloading), "Already downloading: 38%")]
    [InlineData(nameof(UpdateStart.AlreadyDownloaded), nameof(UpdateStage.Downloaded), "2.0.2 is already downloaded: /restart switches to it")]
    [InlineData(nameof(UpdateStart.UpToDate), nameof(UpdateStage.Latest), "You have the latest version: 2.0.1")]
    [InlineData(nameof(UpdateStart.NotInstalledByScript), nameof(UpdateStage.Available), "This Echo was not installed by the install script, so it cannot update itself. The new version is installed with: curl … | sh")]
    public void Run_Update_SaysWhatHappened(string start, string stage, string expected)
    {
        // Arrange
        var updater = new Mock<IUpdater>();
        updater.SetupGet(candidate => candidate.Status).Returns(new UpdateStatus(Enum.Parse<UpdateStage>(stage), "2.0.1", "2.0.2", 0.385));
        updater.Setup(candidate => candidate.Download()).Returns(Enum.Parse<UpdateStart>(start));

        ListenCommands commands = Commands(updater.Object, out ListenActivity activity);

        // Act
        ListenOutcome outcome = commands.Run("/update");

        // Assert
        Assert.Equal(ListenOutcome.Continue, outcome);
        Assert.Equal(["/update", expected], activity.Log.Snapshot().Select(entry => entry.Text));
        updater.Verify(candidate => candidate.Download(), Times.Once);
    }

    [Fact]
    public void Run_Restart_LeavesTheScreenToStartAnew()
    {
        // Arrange
        ListenCommands commands = Commands(Mock.Of<IUpdater>(), out _);

        // Act & Assert
        Assert.Equal(ListenOutcome.Restart, commands.Run("/restart"));
    }

    [Theory]
    [InlineData(nameof(UpdateNoticeKind.DownloadStarted), nameof(EntryKind.Answer), "Downloading Echo 2.0.2: the top line shows how far it has got")]
    [InlineData(nameof(UpdateNoticeKind.Downloaded), nameof(EntryKind.Answer), "Echo 2.0.2 is downloaded. /restart switches to it now; otherwise it starts next time")]
    [InlineData(nameof(UpdateNoticeKind.DownloadFailed), nameof(EntryKind.Warning), "Could not download Echo 2.0.2: no route to host. /update tries again")]
    [InlineData(nameof(UpdateNoticeKind.UpToDate), nameof(EntryKind.Answer), "You have the latest version: 2.0.2")]
    [InlineData(nameof(UpdateNoticeKind.CheckFailed), nameof(EntryKind.Warning), "Could not find out whether a new version is out: no route to host")]
    public void Tell_WritesTheNoticeInTheInterfaceLanguage(string kind, string entryKind, string expected)
    {
        // Arrange
        var log = new ScreenLog();

        // Act
        ListenCommands.Tell(log, new UpdateNotice(Enum.Parse<UpdateNoticeKind>(kind), "2.0.2", "no route to host"));

        // Assert
        ScreenEntry entry = Assert.Single(log.Snapshot());

        Assert.Equal(Enum.Parse<EntryKind>(entryKind), entry.Kind);
        Assert.Equal(expected, entry.Text);
    }

    [Theory]
    [InlineData(nameof(UpdateStage.Unknown), "2.0.1")]
    [InlineData(nameof(UpdateStage.Latest), "2.0.1, the latest")]
    [InlineData(nameof(UpdateStage.Available), "2.0.1, 2.0.2 is out")]
    [InlineData(nameof(UpdateStage.Downloading), "2.0.1, downloading 2.0.2: 38%")]
    [InlineData(nameof(UpdateStage.Downloaded), "2.0.1, 2.0.2 is downloaded")]
    public void DescribeVersion_SaysWhatThereIsAboutANewerOne(string stage, string expected)
    {
        // Act
        string described = ListenCommands.DescribeVersion(new UpdateStatus(Enum.Parse<UpdateStage>(stage), "2.0.1", "2.0.2", 0.385));

        // Assert
        Assert.Equal(expected, described);
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

    private static ListenCommands Commands(IUpdater updater, out ListenActivity activity)
    {
        activity = new ListenActivity(new ScreenLog(), () => false);

        return new ListenCommands(
            activity,
            new LoggingLevelSwitch(LogEventLevel.Debug),
            Mock.Of<IUserSettingsStore>(),
            () => Mock.Of<ITextInsertionService>(),
            () => [],
            updater,
            "curl … | sh");
    }
}