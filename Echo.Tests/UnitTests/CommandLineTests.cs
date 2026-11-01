namespace Echo.Tests.UnitTests;

[Collection(CultureCollection.Name)]
public sealed class CommandLineTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;

    public CommandLineTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalCulture;
    }

    [Fact]
    public void Suggestions_OffersTheCommandsThatStartWithWhatIsTyped()
    {
        // Arrange
        var line = new CommandLine(Commands(out _, out _).All);

        // Act & Assert
        Type(line, "/p");
        Assert.Equal(["/pause"], line.Suggestions.Select(row => row.Label));

        line.Clear();
        Type(line, "/log debug");
        Assert.Empty(line.Suggestions);
        Assert.Equal("/log debug", line.Submit());
        Assert.Equal(string.Empty, line.Text);

        Type(line, "hello");
        Assert.Empty(line.Suggestions);
    }

    [Fact]
    public void DeleteWord_TakesAwayTheLastWordWithTheSpacesAfterIt()
    {
        // Arrange
        var line = new CommandLine(Commands(out _, out _).All);
        Type(line, "/log debug  ");

        // Act & Assert
        line.DeleteWord();
        Assert.Equal("/log ", line.Text);

        line.DeleteWord();
        Assert.Equal(string.Empty, line.Text);

        line.DeleteWord();
        Assert.Equal(string.Empty, line.Text);
    }

    private static ListenCommands Commands(out ListenActivity activity, out LoggingLevelSwitch levelSwitch)
    {
        activity = new ListenActivity(new ScreenLog(), () => false);
        levelSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);

        return new ListenCommands(activity, levelSwitch, Mock.Of<IUserSettingsStore>(store => store.Load() == new UserSettings()), () => Mock.Of<ITextInsertionService>(), () => [("Model", "ggml-tiny.bin"), ("Microphone", "Desk microphone")]);
    }

    private static void Type(CommandLine line, string text)
    {
        foreach (char character in text)
            line.Type(character);
    }
}