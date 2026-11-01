namespace Echo.Tests.UnitTests;

public sealed class TerminalInputParserTests
{
    [Theory]
    [InlineData("\u001b[", true)]
    [InlineData("\u001b[", false)]
    [InlineData("\u001bO", true)]
    [InlineData("\u001bO", false)]
    [InlineData("\u001b[1;", true)]
    [InlineData("\u001b[1;", false)]
    [InlineData("\u001b[?1004;", true)]
    [InlineData("\u001b[?1004;", false)]
    [InlineData("\u001b[?1004;1$", true)]
    [InlineData("\u001b[?1004;1$", false)]
    public void ReadKey_IncompletePrefixExpires_PreservesTheNextCommandWithOrWithoutPolling(string prefix, bool poll)
    {
        var reports = new TerminalFocusReports();
        var clock = new ManualClock();
        var parser = new TerminalInputParser(reports, clock);
        parser.Feed(Encoding.UTF8.GetBytes(prefix));
        Assert.Null(parser.ReadKey());
        clock.Advance(TimeSpan.FromSeconds(5));
        if (poll)
            while (parser.ReadKey() is not null) { }
        parser.Feed("/quit\r"u8);
        var text = new StringBuilder();
        while (parser.ReadKey() is { } key)
            if (!key.Modifiers.HasFlag(ConsoleModifiers.Alt))
                text.Append(key.KeyChar);
        Assert.Equal("/quit\r", text.ToString());
        Assert.Null(reports.IsFocused);
    }

    [Theory]
    [InlineData("\u001b[", '[')]
    [InlineData("\u001bO", 'O')]
    public void ReadKey_AmbiguousAltKey_ReturnsTheKeyAfterABoundedWait(string prefix, char character)
    {
        var clock = new ManualClock();
        var parser = new TerminalInputParser(new TerminalFocusReports(), clock);
        parser.Feed(Encoding.UTF8.GetBytes(prefix));
        Assert.Null(parser.ReadKey());
        clock.Advance(TimeSpan.FromMilliseconds(300));
        ConsoleKeyInfo key = parser.ReadKey()!.Value;
        Assert.Equal(character, key.KeyChar);
        Assert.True(key.Modifiers.HasFlag(ConsoleModifiers.Alt));
        Assert.Null(parser.ReadKey());
    }

    [Theory]
    [InlineData("\u001bO/quit\r", "/quit\r")]
    [InlineData("\u001b[1;\u0003/quit\r", "\u0003/quit\r")]
    [InlineData("\u001b[1;Ж/quit\r", "Ж/quit\r")]
    public void ReadKey_InvalidControlStructure_PreservesTheFirstNonSequenceCharacter(string input, string expected)
    {
        var parser = new TerminalInputParser(new TerminalFocusReports(), new ManualClock());
        parser.Feed(Encoding.UTF8.GetBytes(input));
        var text = new StringBuilder();
        while (parser.ReadKey() is { } key)
            text.Append(key.KeyChar);
        Assert.Equal(expected, text.ToString());
    }

    [Theory]
    [InlineData('I', true)]
    [InlineData('O', false)]
    public void ReadKey_FragmentedFocusWithinTheWait_RemainsAReport(char final, bool focused)
    {
        var reports = new TerminalFocusReports();
        var clock = new ManualClock();
        var parser = new TerminalInputParser(reports, clock);
        clock.Advance(TimeSpan.FromMinutes(2));
        parser.Feed("\u001b"u8);
        Assert.Null(parser.ReadKey());
        clock.Advance(TimeSpan.FromMilliseconds(10));
        parser.Feed("["u8);
        Assert.Null(parser.ReadKey());
        clock.Advance(TimeSpan.FromMilliseconds(10));
        parser.Feed([(byte)final]);
        Assert.Null(parser.ReadKey());
        Assert.Equal(focused, reports.IsFocused);
    }

    [Fact]
    public void ReadKey_LateAndFragmentedFocusEvents_AreReportsRatherThanKeys()
    {
        var reports = new TerminalFocusReports();
        var clock = new ManualClock();
        var parser = new TerminalInputParser(reports, clock);
        clock.Advance(TimeSpan.FromMinutes(2));
        foreach (byte b in Encoding.UTF8.GetBytes("\u001b[O"))
        {
            parser.Feed([b]);
            Assert.Null(parser.ReadKey());
        }
        Assert.False(reports.IsFocused);
        parser.Feed("\u001b[I"u8);
        Assert.Null(parser.ReadKey());
        Assert.True(reports.IsFocused);
    }

    [Theory]
    [InlineData("\u001b[5~", ConsoleKey.PageUp, (ConsoleModifiers)0)]
    [InlineData("\u001b[6~", ConsoleKey.PageDown, (ConsoleModifiers)0)]
    [InlineData("\u001bOR", ConsoleKey.F3, (ConsoleModifiers)0)]
    [InlineData("\u001b[13~", ConsoleKey.F3, (ConsoleModifiers)0)]
    [InlineData("\u001b[A", ConsoleKey.UpArrow, (ConsoleModifiers)0)]
    [InlineData("\u001b[1;5D", ConsoleKey.LeftArrow, ConsoleModifiers.Control)]
    [InlineData("\u001b[3;3~", ConsoleKey.Delete, ConsoleModifiers.Alt)]
    [InlineData("\u001b[Z", ConsoleKey.Tab, ConsoleModifiers.Shift)]
    [InlineData("\u001b[H", ConsoleKey.Home, (ConsoleModifiers)0)]
    [InlineData("\u001b[F", ConsoleKey.End, (ConsoleModifiers)0)]
    public void ReadKey_RealTerminalKey_IsPreservedWithoutChangingFocus(string sequence, ConsoleKey key, ConsoleModifiers modifiers)
    {
        var reports = new TerminalFocusReports();
        var parser = new TerminalInputParser(reports, TimeProvider.System);
        parser.Feed(Encoding.UTF8.GetBytes(sequence));
        ConsoleKeyInfo result = parser.ReadKey()!.Value;
        Assert.Equal(key, result.Key);
        Assert.Equal(modifiers, result.Modifiers);
        Assert.Null(reports.IsFocused);
    }

    [Fact]
    public void ReadKey_Utf8SplitAtEveryByte_PreservesAllCharacters()
    {
        var parser = new TerminalInputParser(new TerminalFocusReports(), TimeProvider.System);
        const string expected = "Привет 世界 🌍";
        var text = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(expected))
        {
            parser.Feed([b]);
            while (parser.ReadKey() is { } key)
                text.Append(key.KeyChar);
        }
        Assert.Equal(expected, text.ToString());
    }

    [Theory]
    [InlineData("\u0003", ConsoleKey.C, ConsoleModifiers.Control)]
    [InlineData("\u0017", ConsoleKey.W, ConsoleModifiers.Control)]
    [InlineData("\u0008", ConsoleKey.Backspace, ConsoleModifiers.Control)]
    [InlineData("\u007f", ConsoleKey.Backspace, (ConsoleModifiers)0)]
    [InlineData("\r", ConsoleKey.Enter, (ConsoleModifiers)0)]
    [InlineData("\u001bx", ConsoleKey.X, ConsoleModifiers.Alt)]
    public void ReadKey_ControlAndAltKeys_KeepTheirMeaning(string sequence, ConsoleKey key, ConsoleModifiers modifiers)
    {
        var parser = new TerminalInputParser(new TerminalFocusReports(), TimeProvider.System);
        parser.Feed(Encoding.UTF8.GetBytes(sequence));
        ConsoleKeyInfo result = parser.ReadKey()!.Value;
        Assert.Equal(key, result.Key);
        Assert.Equal(modifiers, result.Modifiers);
    }

    [Fact]
    public void ReadKey_LoneEscape_WaitsBrieflyWithoutBlockingAndThenReturnsEscape()
    {
        var clock = new ManualClock();
        var parser = new TerminalInputParser(new TerminalFocusReports(), clock);
        parser.Feed("\u001b"u8);
        Assert.Null(parser.ReadKey());
        clock.Advance(TimeSpan.FromMilliseconds(60));
        Assert.Equal(ConsoleKey.Escape, parser.ReadKey()!.Value.Key);
    }

    [Fact]
    public void Feed_KeyAfterExpiredEscape_DoesNotTurnItIntoAnAltCombination()
    {
        var clock = new ManualClock();
        var parser = new TerminalInputParser(new TerminalFocusReports(), clock);
        parser.Feed("\u001b"u8);
        clock.Advance(TimeSpan.FromMilliseconds(60));
        parser.Feed("x"u8);
        Assert.Equal(ConsoleKey.Escape, parser.ReadKey()!.Value.Key);
        ConsoleKeyInfo key = parser.ReadKey()!.Value;
        Assert.Equal('x', key.KeyChar);
        Assert.Equal((ConsoleModifiers)0, key.Modifiers);
    }

    [Fact]
    public void ReadKey_CompleteFocusBufferedBeforeAPause_IsNotAnExpiredPrefix()
    {
        var reports = new TerminalFocusReports();
        var clock = new ManualClock();
        var parser = new TerminalInputParser(reports, clock);
        parser.Feed("\u001b[O"u8);
        clock.Advance(TimeSpan.FromSeconds(5));
        parser.Feed("/quit\r"u8);
        Assert.Equal('/', parser.ReadKey()!.Value.KeyChar);
        Assert.False(reports.IsFocused);
    }

    [Fact]
    public void ReadKey_FocusBetweenKeys_DoesNotConsumeTheKeys()
    {
        var reports = new TerminalFocusReports();
        var parser = new TerminalInputParser(reports, TimeProvider.System);
        parser.Feed("a\u001b[Ob\u001b[Ic"u8);
        Assert.Equal('a', parser.ReadKey()!.Value.KeyChar);
        Assert.Equal('b', parser.ReadKey()!.Value.KeyChar);
        Assert.False(reports.IsFocused);
        Assert.Equal('c', parser.ReadKey()!.Value.KeyChar);
        Assert.True(reports.IsFocused);
    }

    [Fact]
    public void ReadKey_ModeReply_DoesNotBecomeAKeyOrAFocusState()
    {
        var reports = new TerminalFocusReports();
        var parser = new TerminalInputParser(reports, TimeProvider.System);
        parser.Feed("\u001b[?1004;1$y"u8);
        Assert.Null(parser.ReadKey());
        Assert.Null(reports.IsFocused);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan time) => _ticks += time.Ticks;
    }
}