using System.Globalization;
using Echo.Listening;
using Echo.UI;
using Echo.Updates;
using Serilog.Core;
using Serilog.Events;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Echo.Tests.UnitTests;

[Collection(CultureCollection.Name)]
public sealed class ListenFrameTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;

    public ListenFrameTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalCulture;
    }

    private static readonly DateTimeOffset _time = new(new DateTime(2026, 10, 14, 21, 3, 0, DateTimeKind.Local));

    [Fact]
    public void Build_Ready_HasTheStateOnTopTheLogTheCommandBoxAndTheKeysAtTheBottom()
    {
        // Arrange
        ListenView view = View(ListenState.Listening) with
        {
            Entries =
            [
                new(_time, EntryKind.Message, "Push-to-talk released"),
                new(_time, EntryKind.Dictated, "hello, this is a test", "0.42 s"),
                new(_time, EntryKind.Warning, "Audio was silent or empty"),
            ],
        };

        // Act
        string[] lines = Render(view, width: 100, height: 30);

        // Assert: one row less than the terminal, so writing it never scrolls
        Assert.Equal(29, lines.Length);

        // The state says Echo is ready, not that it is listening: nothing is heard until the key is held.
        // Only the key and the model follow it, the key first and with nothing in front of it; the rest is in /info
        Assert.Matches("^  ○ ready +--:--  ┊  ··········  ┊  `  ┊  tiny$", lines[0].TrimEnd());

        // The rule lies right under the state, and the log right under the rule
        Assert.StartsWith("──────", lines[1]);

        Assert.Equal("  21:03  Push-to-talk released", lines[2].TrimEnd());
        Assert.Equal("  21:03  └ hello, this is a test  0.42 s", lines[3].TrimEnd());
        Assert.Equal("  21:03  ! Audio was silent or empty", lines[4].TrimEnd());

        // The command box and the keys close the screen; the empty box shows its cursor in front of the hint
        Assert.StartsWith("╭", lines[^4]);
        Assert.Contains("│ > █type / for commands", lines[^3]);
        Assert.StartsWith("╰", lines[^2]);
        Assert.Equal("  Enter run  ┊  ↑↓ PgUp PgDn log  ┊  Ctrl+C quit", lines[^1].TrimEnd());

        // Every row fits the terminal, so nothing wraps on its own
        Assert.All(lines, line => Assert.True(ListenFrame.WidthOf(line) < 100, line));
    }

    [Fact]
    public void Build_NotRecording_ShowsStillDotsWhereTheEqualizerIs()
    {
        // Act
        string[] moments = [.. new[] { 0.0, 0.6, 1.2 }.Select(seconds => Render(View(ListenState.Listening) with { Clock = TimeSpan.FromSeconds(seconds) }, width: 100, height: 30)[0])];

        // Assert: nothing moves while nothing is recorded
        Assert.All(moments, line => Assert.Matches("○ ready +--:--  ┊  ··········  ┊  `  ┊  tiny", line));
        Assert.Single(moments.Distinct());
        Assert.Contains("··········", Render(View(ListenState.Paused), width: 100, height: 30)[0]);
    }

    [Theory]
    [InlineData(0f, "····👂····")]
    [InlineData(0.01f, "····👂····")]
    [InlineData(0.012f, "···(👂)···")]
    [InlineData(0.1f, "··((👂))··")]
    [InlineData(0.5f, "((((👂))))")]
    public void Build_Recording_ArcsSpreadFromThePointWithTheVoice(float level, string expected)
    {
        // Arrange: the voice at this level for the latest pieces of sound
        ListenView view = View(ListenState.Recording) with
        {
            RecordingTime = TimeSpan.FromSeconds(2),
            Levels = [0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, level, level, level],
        };

        // Act
        string state = Render(view, width: 100, height: 30)[0];

        // Assert: ten columns, so everything after them stays where it is while ready
        Assert.Contains($"00:02  ┊  {expected}  ┊  `  ┊  tiny", state);
        Assert.Matches("● recording +00:02", state);

        string ready = Render(View(ListenState.Listening), width: 100, height: 30)[0];

        Assert.Equal(ready.IndexOf("┊  tiny", StringComparison.Ordinal), state.IndexOf("┊  tiny", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Recognizing_KeepsThePointWithoutArcs()
    {
        // Act
        string state = Render(View(ListenState.Transcribing), width: 100, height: 30)[0];

        // Assert
        Assert.Matches("⠋ recognizing +--:--  ┊  ····👂····  ┊  `  ┊  tiny", state);
    }

    [Fact]
    public void Build_Recognizing_TheDotsOfItsMarkGoRound()
    {
        // Act
        string[] marks =
        [
            .. new[] { 0, 100, 250, 1000 }.Select(milliseconds =>
                Render(View(ListenState.Transcribing) with { Clock = TimeSpan.FromMilliseconds(milliseconds) }, width: 100, height: 30)[0].Trim()[..1]),
        ];

        // Assert: a step every 100 ms, the whole turn in a second
        Assert.Equal(["⠋", "⠙", "⠹", "⠋"], marks);
    }

    [Fact]
    public void Build_NarrowTerminal_LeavesOutTheModelBeforeTheKey()
    {
        // Act
        string narrow = Render(View(ListenState.Listening), width: 50, height: 20)[0];
        string narrower = Render(View(ListenState.Listening), width: 40, height: 20)[0];

        // Assert: whole pieces go, the model first, which /info shows too
        Assert.DoesNotContain("tiny", narrow);
        Assert.EndsWith("┊  `", narrow.TrimEnd());

        Assert.DoesNotContain("`", narrower);
        Assert.Contains("○ ready", narrower);
    }

    [Fact]
    public void Build_PanelOpen_ShowsItsRowsWithAirUnderThemAndItsOwnKeys()
    {
        // Arrange
        ListenCommands commands = Commands(out _, out _);
        var line = new CommandLine(commands.All);
        line.Type('/');

        ListenView view = View(ListenState.Listening) with { Input = line.Text, Panel = line.Suggestions, Selected = 1 };

        // Act
        string[] lines = Render(view, width: 100, height: 30);

        // Assert: as many commands as the panel shows, the names in one column, the highlighted one second
        int box = Array.FindIndex(lines, row => row.Contains("│ > /█"));

        Assert.Equal(
            ["/setup", "/info", "/copy", "/pause", "/resume", "/log", "/update", "/restart"],
            lines[(box + 2)..(box + 10)].Select(row => row.Trim().Split(' ')[0]));

        Assert.Equal("   /info      summary of the main settings", lines[box + 3].TrimEnd());
        Assert.Equal(string.Empty, lines[^2].Trim());
        Assert.Equal("  Enter choose  ┊  ↑↓ select  ┊  Esc close", lines[^1].TrimEnd());
    }

    [Fact]
    public void Build_LongLine_IsWrappedUnderItsOwnText()
    {
        // Arrange
        string text = string.Join(' ', Enumerable.Repeat("word", 30));
        ListenView view = View(ListenState.Listening) with { Entries = [new(_time, EntryKind.Warning, text)] };

        // Act
        string[] lines = Render(view, width: 60, height: 20);

        // Assert: continuation rows start under the text, not under the time
        Assert.StartsWith("  21:03  ! word word", lines[2]);
        Assert.StartsWith("           word word", lines[3]);
        Assert.Equal(text, string.Join(' ', lines[2..6].Select(row => row[11..].Trim())).Trim());
    }

    [Fact]
    public void Build_ScrolledBack_ShowsOlderLinesAndHowToGetToTheBottom()
    {
        // Arrange
        ScreenEntry[] entries = [.. Enumerable.Range(1, 60).Select(index => new ScreenEntry(_time, EntryKind.Message, $"line {index}"))];
        ListenView view = View(ListenState.Listening) with { Entries = entries, ScrollRows = 10 };

        // Act
        var frame = new ListenFrame(Theme.Full);
        ListenFrameResult result = frame.Build(view, 100, 30);
        string[] lines = Render(view, width: 100, height: 30);

        // Assert
        Assert.Equal(10, result.ScrollRows);
        Assert.EndsWith(" End to the very bottom ──", lines[1].TrimEnd());
        Assert.Contains("line 50", lines[2 + result.LogRows - 1]);

        // Further back than there is, the log stops at its first line
        ListenFrameResult top = frame.Build(view with { ScrollRows = 1000 }, 100, 30);

        Assert.Equal(60 - top.LogRows, top.ScrollRows);
    }

    [Fact]
    public void Build_APathTooLongForItsRow_GoesWholeOntoTheNextRow()
    {
        // Arrange: a full path of the system the test runs on
        string path = OperatingSystem.IsWindows()
            ? @"C:\Users\Someone\AppData\Roaming\echooo\settings.json"
            : "/home/someone/.config/echooo/settings.json";

        ListenView view = View(ListenState.Listening) with
        {
            Entries = [new(_time, EntryKind.Answer, "Settings file                   " + path, Indent: 32)],
        };

        // Act
        string[] lines = Render(view, width: 80, height: 20);

        // Assert: the label on its row, the path in one piece under it, so it is copied as it is
        Assert.Equal("  21:03  Settings file", lines[2].TrimEnd());
        Assert.Equal("           " + path, lines[3].TrimEnd());
    }

    [Fact]
    public void Build_StepsOfAGroup_HangOnTheLinesOfATreeUnderTheirHeading()
    {
        // Arrange
        ScreenEntry Step(string text, int depth = 1, MessageTone tone = MessageTone.Plain) =>
            new(_time, EntryKind.Message, text) { Depth = depth, Tone = tone };

        ListenView view = View(ListenState.Listening) with
        {
            Entries =
            [
                Step("Audio:", depth: 0, MessageTone.Heading),
                Step("Analyzing audio devices...", tone: MessageTone.Progress),
                Step("Successfuly analized audio devices.", tone: MessageTone.Done),
                Step("Push-to-talk pressed.", depth: 0, MessageTone.Key),
                Step("Microphone activated, recording audio..."),
            ],
        };

        // Act
        string[] lines = Render(view, width: 100, height: 30);

        // Assert: the last step of each group ends its tree
        Assert.Equal(
            [
                "  21:03  ▸ Audio:",
                "  21:03  ├─ Analyzing audio devices...",
                "  21:03  └─ Successfuly analized audio devices.",
                "  21:03  Push-to-talk pressed.",
                "  21:03  └─ Microphone activated, recording audio...",
            ],
            lines[2..7].Select(line => line.TrimEnd()));
    }

    [Fact]
    public void Rows_AnswerNamingALevel_DrawsTheLevelInItsColor()
    {
        // Arrange
        var entry = new ScreenEntry(_time, EntryKind.Answer, "Log detail: warning.") { Accents = [(12, 7, Theme.Warning)] };

        // Act
        string row = new ListenFrame(Theme.Full).Rows(entry, width: 100)[0];

        // Assert: only the level has the color of its own, the rest of the answer the color of the log
        Assert.Contains($"[{Theme.Warning}]warning[/]", row);
        Assert.EndsWith($"[{Theme.Warning}]warning[/][#c9d1d9].[/]", row);
        Assert.Equal("  21:03  Log detail: warning.", Markup.Remove(row));
    }

    [Fact]
    public void Rows_LinesOfTheRecognitionLibraryAndOfTheKey_StandApartByTheirColor()
    {
        // Arrange
        var frame = new ListenFrame(Theme.Full);

        // Act
        string library = frame.Rows(new ScreenEntry(_time, EntryKind.Message, "whisper_backend_init_gpu: using CUDA0 backend") { Depth = 1, Tone = MessageTone.Library }, width: 100)[0];
        string key = frame.Rows(new ScreenEntry(_time, EntryKind.Message, "Recording...") { Tone = MessageTone.Key }, width: 100)[0];

        // Assert: what the library says about itself is grey and faint, a press of the key green
        Assert.Contains("[#6e7681]whisper_backend_init_gpu: using CUDA0 backend[/]", library);
        Assert.Contains($"[bold {Theme.Success}]Recording...[/]", key);
    }

    [Theory]
    [InlineData("en", 40, 10, false)]
    [InlineData("en", 40, 10, true)]
    [InlineData("en", 60, 13, true)]
    [InlineData("de", 40, 12, true)]
    [InlineData("ru", 40, 12, true)]
    [InlineData("es", 80, 24, true)]
    [InlineData("zh-Hans", 40, 12, true)]
    [InlineData("en", 39, 30, false)]
    [InlineData("en", 80, 9, true)]
    [InlineData("en", 20, 5, false)]
    public void Build_AnySize_StaysWithinTheTerminal(string language, int width, int height, bool isPanelOpen)
    {
        // Arrange
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);

        ListenView view = View(ListenState.Recording) with
        {
            Entries = [.. Enumerable.Range(1, 30).Select(line => new ScreenEntry(_time, EntryKind.Message, $"line {line} of a log that is long enough to wrap"))],
            Input = isPanelOpen ? "/" : string.Empty,
            Panel = isPanelOpen
                ? [.. Enumerable.Range(1, ListenFrame.MaxPanelRows).Select(row => new PanelRow($"/command{row}", $"/command{row}", "what the command does, at some length"))]
                : [],
        };

        // Act
        ListenFrameResult frame = new ListenFrame(Theme.Full).Build(view, width, height);

        // Assert: the last row stays free, and no row reaches the right edge
        Assert.InRange(frame.Lines.Count, 1, height - 1);
        Assert.All(frame.Lines, line => Assert.True(ListenFrame.WidthOf(Markup.Remove(line)) < width, Markup.Remove(line)));
    }

    [Fact]
    public void Build_TerminalTooSmall_AsksToMakeItLarger()
    {
        // Act
        string[] lines = Render(View(ListenState.Listening), width: 30, height: 8);

        // Assert
        Assert.Equal(["Window too small: make it", "larger"], lines.Select(line => line.TrimEnd()));
    }

    [Fact]
    public void Build_MessageOfSeveralLines_TakesARowForEachOfThem()
    {
        // Arrange: an error message, the way an exception may word it
        ListenView view = View(ListenState.Listening) with
        {
            Entries = [new(_time, EntryKind.Error, "first line\r\nsecond\tline\n")],
        };

        // Act
        string[] lines = Render(view, width: 80, height: 20);

        // Assert: every line of the message has a row of its own, under its own text
        Assert.Equal("  21:03  × first line", lines[2].TrimEnd());
        Assert.Equal("           second    line", lines[3].TrimEnd());
        Assert.Equal(string.Empty, lines[4].Trim());
    }

    [Fact]
    public void Rows_TableRowWithALineBreakInItsValue_StaysOnOneRowOfItsOwn()
    {
        // Arrange: a device name with a line break, the way /info shows it
        var entry = new ScreenEntry(_time, EntryKind.Answer, "Microphone  first\nsecond", Indent: 12);

        // Act
        List<string> rows = new ListenFrame(Theme.Full).Rows(entry, width: 80);

        // Assert
        Assert.All(rows, row => Assert.DoesNotContain('\n', row));
        Assert.Contains("first second", Markup.Remove(rows[0]));
    }

    [Theory]
    [InlineData(nameof(UpdateStage.Unknown), null, 0, "┊  tiny  ┊  2.0.1")]
    [InlineData(nameof(UpdateStage.Latest), null, 0, "┊  tiny  ┊  √ 2.0.1")]
    [InlineData(nameof(UpdateStage.Available), "2.0.2", 0, "┊  tiny  ┊  2.0.1 (↑)")]
    [InlineData(nameof(UpdateStage.Downloading), "2.0.2", 0.05, "┊  tiny  ┊  2.0.1 (⠋  5%)")]
    [InlineData(nameof(UpdateStage.Downloading), "2.0.2", 0.387, "┊  tiny  ┊  2.0.1 (⠋ 38%)")]
    [InlineData(nameof(UpdateStage.Downloaded), "2.0.2", 0, "┊  tiny  ┊  2.0.1 → 2.0.2 (/restart)")]
    public void Build_Version_ShowsTheOneInUseAndWhatThereIsAboutANewerOne(string stage, string? newer, double progress, string expected)
    {
        // Arrange
        ListenView view = View(ListenState.Listening) with { Update = new UpdateStatus(Enum.Parse<UpdateStage>(stage), "2.0.1", newer, progress) };

        // Act
        string state = Render(view, width: 100, height: 30)[0];

        // Assert: the version always comes last, after the model
        Assert.EndsWith(expected, state.TrimEnd());
    }

    [Fact]
    public void Build_NewerVersionOut_TheArrowBlinks()
    {
        // Arrange
        ListenView view = View(ListenState.Listening) with { Update = new UpdateStatus(UpdateStage.Available, "2.0.1", "2.0.2") };
        var frame = new ListenFrame(Theme.Full);

        // Act
        string[] moments = [.. new[] { 0, 650, 700, 1400 }.Select(milliseconds => frame.Build(view with { Clock = TimeSpan.FromMilliseconds(milliseconds) }, 100, 30).Lines[0])];

        // Assert: purple for 0.7 s, then as dark as the still dots, then purple again
        Assert.Contains($"[bold {Theme.Accent}]↑[/]", moments[0]);
        Assert.Contains($"[bold {Theme.Accent}]↑[/]", moments[1]);
        Assert.Contains("[bold #3d4450]↑[/]", moments[2]);
        Assert.Contains($"[bold {Theme.Accent}]↑[/]", moments[3]);
    }

    [Fact]
    public void Build_Downloading_TheMarkGoesRoundLikeTheOneOfRecognizing()
    {
        // Arrange
        ListenView view = View(ListenState.Listening) with { Update = new UpdateStatus(UpdateStage.Downloading, "2.0.1", "2.0.2", 0.5) };

        // Act
        string[] marks =
        [
            .. new[] { 0, 100, 250 }.Select(milliseconds =>
                Render(view with { Clock = TimeSpan.FromMilliseconds(milliseconds) }, width: 100, height: 30)[0].TrimEnd()[^6..^5]),
        ];

        // Assert
        Assert.Equal(["⠋", "⠙", "⠹"], marks);
    }

    [Fact]
    public void Build_NewerVersionOut_OffersUpdateAmongTheKeys()
    {
        // Arrange
        ListenView view = View(ListenState.Listening) with { Update = new UpdateStatus(UpdateStage.Available, "2.0.1", "2.0.2") };

        // Act
        string wide = Render(view, width: 100, height: 30)[^1].TrimEnd();
        string narrow = Render(view, width: 50, height: 30)[^1].TrimEnd();
        string narrower = Render(view, width: 45, height: 30)[^1].TrimEnd();
        string downloading = Render(view with { Update = view.Update! with { Stage = UpdateStage.Downloading } }, width: 100, height: 30)[^1].TrimEnd();

        // Assert: before quitting; on a narrow terminal the log keys and Enter go first, /update stays
        Assert.Equal("  Enter run  ┊  ↑↓ PgUp PgDn log  ┊  /update download  ┊  Ctrl+C quit", wide);
        Assert.Equal("  Enter run  ┊  /update download  ┊  Ctrl+C quit", narrow);
        Assert.Equal("  /update download  ┊  Ctrl+C quit", narrower);
        Assert.Equal("  Enter run  ┊  ↑↓ PgUp PgDn log  ┊  Ctrl+C quit", downloading);
        Assert.Contains($"[bold {Theme.Highlight}]/update download[/]", new ListenFrame(Theme.Full).Build(view, 100, 30).Lines[^1]);
    }

    [Fact]
    public void Build_NarrowTerminal_KeepsTheVersionLongest()
    {
        // Arrange
        ListenView view = View(ListenState.Listening) with { Update = new UpdateStatus(UpdateStage.Latest, "2.0.1") };

        // Act
        string narrow = Render(view, width: 50, height: 20)[0].TrimEnd();
        string narrower = Render(view, width: 40, height: 20)[0].TrimEnd();

        // Assert: the model goes first, then the key, then the equalizer
        Assert.DoesNotContain("tiny", narrow);
        Assert.DoesNotContain("`", narrow);
        Assert.EndsWith("··········  ┊  √ 2.0.1", narrow);
        Assert.Matches("^  ○ ready +--:--  ┊  √ 2.0.1$", narrower);
    }

    [Fact]
    public void Build_TerminalOfDefaultWidth_FitsEverythingInEveryLanguage()
    {
        // Arrange: the longest version text, a long model name and a long key
        ListenView view = View(ListenState.Transcribing) with
        {
            Model = "large-v3-turbo",
            Key = "Right Ctrl+Space",
            Update = new UpdateStatus(UpdateStage.Downloaded, "2.0.10", "2.0.11"),
        };

        foreach (string language in new[] { "en", "ru", "zh-Hans", "es", "de" })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);

            // Act: 120 columns is what Windows Terminal opens with
            string state = Render(view, width: 120, height: 30)[0];

            // Assert
            Assert.Contains("large-v3-turbo", state);
            Assert.EndsWith("2.0.10 → 2.0.11 (/restart)", state.TrimEnd());
        }
    }

    [Fact]
    public void Build_TerminalWithoutUnicode_DrawsTheVersionInPlainLetters()
    {
        // Arrange
        ListenView available = View(ListenState.Listening) with { Update = new UpdateStatus(UpdateStage.Available, "2.0.1", "2.0.2") };
        ListenView downloaded = available with { Update = available.Update! with { Stage = UpdateStage.Downloaded } };
        ListenView latest = available with { Update = new UpdateStatus(UpdateStage.Latest, "2.0.1") };

        // Act
        string Line(ListenView view) => Markup.Remove(new ListenFrame(Theme.Ascii).Build(view, 100, 30).Lines[0]).TrimEnd();

        // Assert
        Assert.EndsWith("|  2.0.1 (^)", Line(available));
        Assert.EndsWith("|  2.0.1 -> 2.0.2 (/restart)", Line(downloaded));
        Assert.EndsWith("|  + 2.0.1", Line(latest));
    }

    private static ListenView View(ListenState state) => new(
        state,
        TimeSpan.Zero,
        TimeSpan.Zero,
        [],
        "tiny",
        "`",
        [],
        ScrollRows: 0,
        Input: string.Empty,
        Panel: [],
        Selected: 0);

    private static ListenCommands Commands(out ListenActivity activity, out LoggingLevelSwitch levelSwitch)
    {
        activity = new ListenActivity(new ScreenLog(), () => false);
        levelSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);

        return new ListenCommands(activity, levelSwitch, Mock.Of<IUserSettingsStore>(store => store.Load() == new UserSettings()), () => Mock.Of<ITextInsertionService>(), () => [("Model", "ggml-tiny.bin"), ("Microphone", "Desk microphone")]);
    }

    /// <summary>
    ///     The screen as plain text, one string per row.
    /// </summary>
    private static string[] Render(ListenView view, int width, int height)
    {
        ListenFrameResult frame = new ListenFrame(Theme.Full).Build(view, width, height);
        var console = new TestConsole().Width(width);

        foreach (string line in frame.Lines)
        {
            console.Write(new Markup(line));
            console.WriteLine();
        }

        return [.. console.Output.Replace("\r", string.Empty).TrimEnd('\n').Split('\n')];
    }
}