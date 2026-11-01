namespace Echo.Tests.UnitTests;

public sealed class StartupScreenTests
{
    [Fact]
    public void BuildFrames_FirstFrame_ShowsOnlyFirstLetter()
    {
        // Act
        StartupAnimation.Frame firstFrame = StartupAnimation.BuildFrames()[0];

        // Assert
        Assert.Equal('e', firstFrame.Slots[0].Glyph);
        Assert.All(firstFrame.Slots.Skip(1), slot => Assert.Equal(StartupAnimation.NoGlyph, slot.Glyph));
    }

    [Fact]
    public void BuildFrames_LastFrame_ShowsWordEndingWithEarAndNoEcho()
    {
        // Act
        StartupAnimation.Frame lastFrame = StartupAnimation.BuildFrames()[^1];

        // Assert
        Assert.Equal(
            ['e', 'c', 'h', StartupAnimation.EarGlyph, StartupAnimation.NoGlyph, StartupAnimation.NoGlyph],
            lastFrame.Slots.Select(slot => slot.Glyph));
        Assert.All(lastFrame.Slots.Take(4), slot => Assert.Equal(1f, slot.Intensity));
        Assert.Empty(lastFrame.Highlights);
    }

    [Fact]
    public void BuildFrames_LastFrame_LeavesEqualizerAsRestingLine()
    {
        // Act
        StartupAnimation.Frame lastFrame = StartupAnimation.BuildFrames()[^1];

        // Assert
        Assert.NotNull(lastFrame.Bars);
        Assert.All(lastFrame.Bars, height => Assert.Equal(0, height));
    }

    [Fact]
    public void BuildFrames_Rain_DropsDoNotFallAllAtOnce()
    {
        // Act
        StartupAnimation.Frame firstRainFrame = StartupAnimation.BuildFrames().First(frame => frame.Highlights.Count > 0);

        // Assert
        int startedColumns = firstRainFrame.Highlights.Select(highlight => highlight.Column).Distinct().Count();

        Assert.InRange(startedColumns, 1, firstRainFrame.Bars!.Length - 1);
    }

    [Fact]
    public void BuildFrames_WholeAnimation_HasSensibleLength()
    {
        // Act
        IReadOnlyList<StartupAnimation.Frame> frames = StartupAnimation.BuildFrames();

        // Assert: the timing constants are meant to be tuned, so only nonsense is rejected
        Assert.All(frames.SkipLast(1), frame => Assert.True(frame.DelayMs > 0));
        Assert.InRange(frames.Sum(frame => frame.DelayMs), 100, 10_000);
    }

    [Fact]
    public void BuildFrames_Phases_FollowEachOtherInOrder()
    {
        // Arrange
        IReadOnlyList<StartupAnimation.Frame> frames = StartupAnimation.BuildFrames();

        int First(Func<StartupAnimation.Frame, bool> condition) => frames.ToList().FindIndex(frame => condition(frame));

        // Act
        int wordTyped = First(frame => frame.Slots[3].Glyph == 'o');
        int firstEcho = First(frame => frame.Slots[4].Glyph == 'o');
        int secondEcho = First(frame => frame.Slots[5].Glyph == 'o');
        int crumbling = First(frame => frame.Slots[3].Dissolve > 0);
        int earAppears = First(frame => frame.Slots[3].Glyph == StartupAnimation.EarGlyph);
        int rain = First(frame => frame.Highlights.Count > 0);
        int equalizer = First(frame => frame.Bars is not null && frame.Bars.Any(height => height > 0));

        // Assert
        Assert.True(wordTyped < firstEcho);
        Assert.True(firstEcho < secondEcho);
        Assert.True(secondEcho < crumbling);
        Assert.True(crumbling < earAppears);
        Assert.True(earAppears <= rain);
        Assert.True(rain < equalizer);
    }

    [Fact]
    public void RenderRows_AnyFrame_KeepsLogoSize()
    {
        // Arrange
        IReadOnlyList<StartupAnimation.Frame> frames = StartupAnimation.BuildFrames();
        int expectedWidth = StartupAnimation.RenderRows(frames[^1], useMarkup: false, Theme.Full.LevelBars)[0].Length;

        // Act & Assert
        foreach (StartupAnimation.Frame frame in frames)
        {
            string[] rows = StartupAnimation.RenderRows(frame, useMarkup: false, Theme.Full.LevelBars);

            Assert.Equal(StartupAnimation.RowsCount, rows.Length);
            Assert.All(rows, row => Assert.Equal(expectedWidth, row.Length));
        }
    }

    [Fact]
    public async Task PlayAsync_OutputIsNotTerminal_WritesNothing()
    {
        // Arrange
        var output = new StringWriter();
        var screen = new StartupScreen(CreateConsole(output, InteractionSupport.No));

        // Act
        await screen.PlayAsync(CancellationToken.None);

        // Assert
        Assert.Empty(output.ToString());
    }

    [Fact]
    public async Task PlayAsync_TerminalCannotAnimate_WritesStillLogo()
    {
        // Arrange
        var output = new StringWriter();
        var screen = new StartupScreen(CreateConsole(output, InteractionSupport.Yes));

        // Act
        await screen.PlayAsync(CancellationToken.None);

        // Assert
        string written = output.ToString();

        Assert.Contains("█", written);
        Assert.Contains(ApplicationInfo.Tagline, written);
        Assert.Contains(ApplicationInfo.Version, written);
    }

    [Fact]
    public async Task PlayAsync_TerminalCanAnimate_FinishesWithColoredLogoAndTagline()
    {
        // Arrange
        var output = new StringWriter();
        var screen = new StartupScreen(CreateConsole(output, InteractionSupport.Yes, AnsiSupport.Yes));

        // Act
        await screen.PlayAsync(CancellationToken.None);

        // Assert
        string written = output.ToString();

        Assert.Contains("\u001b[", written);
        Assert.Contains("█", written);
        Assert.Contains(ApplicationInfo.Tagline, written);
    }

    private static IAnsiConsole CreateConsole(TextWriter output,
                                              InteractionSupport interactive,
                                              AnsiSupport ansi = AnsiSupport.No)
    {
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = ansi,
            ColorSystem = ansi == AnsiSupport.Yes ? ColorSystemSupport.TrueColor : ColorSystemSupport.NoColors,
            Interactive = interactive,
            Out = new AnsiConsoleOutput(output)
        });

        // These tests choose the terminal capabilities, independently of the build server environment.
        console.Profile.Capabilities.Interactive = interactive == InteractionSupport.Yes;
        console.Profile.Capabilities.Ansi = ansi == AnsiSupport.Yes;
        console.Profile.Capabilities.Unicode = true;
        console.Profile.Width = 120;
        console.Profile.Height = 40;
        return console;
    }
}