using System.Globalization;
using Echo.Extensions;
using Echo.Listening;
using Echo.Setup;
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
            var commands = new ListenCommands(activity, new LoggingLevelSwitch(LogEventLevel.Debug), store, () => []);

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

    private static ListenCommands Commands(out ListenActivity activity, out LoggingLevelSwitch levelSwitch)
    {
        activity = new ListenActivity(new ScreenLog(), () => false);
        levelSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);

        return new ListenCommands(activity, levelSwitch, Mock.Of<IUserSettingsStore>(store => store.Load() == new UserSettings()), () => [("Model", "ggml-tiny.bin"), ("Microphone", "Desk microphone")]);
    }
}