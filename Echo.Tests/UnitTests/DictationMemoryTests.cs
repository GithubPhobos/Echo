using System.Text.Json;

namespace Echo.Tests.UnitTests;

/// <summary>
///     The history uses real monthly files, including failures and two independent writers.
/// </summary>
public sealed class DictationMemoryTests : IDisposable
{
    [Theory]
    [InlineData(Whisper.net.LibraryLoader.RuntimeLibrary.Cuda, "cuda")]
    [InlineData(Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan, "vulkan")]
    [InlineData(Whisper.net.LibraryLoader.RuntimeLibrary.Cpu, "cpu")]
    public void Add_ActualRuntime_IsRecordedInsteadOfTheRequestedAutoMode(Whisper.net.LibraryLoader.RuntimeLibrary library, string expected)
    {
        var runtime = new Mock<IRecognitionRuntime>();
        runtime.Setup(r => r.Loaded).Returns(library);
        var whisper = new WhisperSettings { HardwareBackend = "Auto", Temperature = 0.2f, NoContext = true };
        var memory = new DictationMemory(_paths, Options.Create(new MemorySettings()), Options.Create(whisper),
            Options.Create(new RecordingDeviceSettings()), _time.Object, runtime.Object, new WhisperSettings());

        Add(memory, "phrase");

        using JsonDocument entry = JsonDocument.Parse(File.ReadAllText(HistoryFile()));
        Assert.Equal(expected, entry.RootElement.GetProperty("acc").GetString());
        JsonElement custom = entry.RootElement.GetProperty("ws");
        Assert.Equal(2, custom.EnumerateObject().Count());
        Assert.Equal(0.2f, custom.GetProperty("t").GetSingle());
        Assert.True(custom.GetProperty("enc").GetBoolean());
        Assert.False(custom.TryGetProperty("Temperature", out _));
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly Mock<TimeProvider> _time = new();
    private readonly DateTimeOffset _now = new(2026, 10, 31, 12, 34, 56, TimeSpan.Zero);

    /// <summary>
    ///     Gives each test its own history and a fixed local time.
    /// </summary>
    public DictationMemoryTests()
    {
        _paths = new AppPaths(_directory, _directory);
        _time.Setup(clock => clock.LocalTimeZone).Returns(TimeZoneInfo.Utc);
        _time.Setup(clock => clock.GetUtcNow()).Returns(() => _now);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Add_RecognizedPhrase_KeepsTextAndRecognitionSettings()
    {
        // Act
        CreateMemory().Add("Продолжение следует...", TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.75));

        // Assert: even a subtitle-like phrase is kept unchanged, without a classification
        using JsonDocument entry = JsonDocument.Parse(Assert.Single(File.ReadAllLines(HistoryFile())));
        JsonElement root = entry.RootElement;

        Assert.Equal(_now.DateTime, root.GetProperty("ts").GetDateTime());
        Assert.Equal(DateTimeKind.Unspecified, root.GetProperty("ts").GetDateTime().Kind);
        Assert.Equal("Продолжение следует...", root.GetProperty("rt").GetString());
        Assert.Equal(3, root.GetProperty("held").GetDouble());
        Assert.Equal(2, root.GetProperty("dur").GetDouble());
        Assert.Equal(0.75, root.GetProperty("rd").GetDouble());
        Assert.Equal("m10", root.GetProperty("mdl").GetString());
        Assert.Equal(Echo.Constants.ApplicationInfo.Version, root.GetProperty("v").GetString());
        Assert.Equal("cuda", root.GetProperty("acc").GetString());
        Assert.Equal("ru", root.GetProperty("lng").GetString());
        Assert.True(root.GetProperty("et").GetBoolean());
        Assert.Equal(0.03f, root.GetProperty("st").GetSingle());
        Assert.Equal(11, root.EnumerateObject().Count());
    }

    [Fact]
    public void Add_QuotesAndLineBreaks_StayInOneReadableJsonLine()
    {
        // Arrange
        const string text = "Привет, \"Echo\"!\r\n下一行\t🌍";

        // Act
        Add(CreateMemory(), text);

        // Assert
        string line = Assert.Single(File.ReadAllLines(HistoryFile()));
        using JsonDocument entry = JsonDocument.Parse(line);

        Assert.Equal(text, entry.RootElement.GetProperty("rt").GetString());
        Assert.Contains("Привет", line);
        Assert.Contains("下一行", line);
    }

    [Theory]
    [InlineData("{\"rt\":\"interrupted")]
    [InlineData("{\"rt\":\"complete without LF\"}")]
    [InlineData("{\"rt\":\"interrupted\"}\r")]
    public void Add_UnterminatedTail_PreservesOldBytesAndKeepsEveryNewPhraseReadable(string tail)
    {
        // Arrange: a previous append ended before its final LF, possibly leaving a complete object.
        Directory.CreateDirectory(_paths.MemoryDirectory);
        byte[] previous = System.Text.Encoding.UTF8.GetBytes("{\"rt\":\"old complete\"}\n" + tail);
        File.WriteAllBytes(HistoryFile(), previous);

        // Act: reopening with a separate writer must also recover tails left by an earlier process.
        Add(CreateMemory(), "after failure");
        Add(CreateMemory(), "next phrase");

        // Assert: the tail is kept intact, and does not swallow either successfully written phrase.
        byte[] result = File.ReadAllBytes(HistoryFile());
        Assert.Equal(previous, result[..previous.Length]);
        Assert.Equal((byte)'\n', result[previous.Length]);
        string[] lines = File.ReadAllLines(HistoryFile());
        Assert.Equal(4, lines.Length);
        using JsonDocument first = JsonDocument.Parse(lines[0]);
        using JsonDocument after = JsonDocument.Parse(lines[2]);
        using JsonDocument next = JsonDocument.Parse(lines[3]);
        Assert.Equal("old complete", first.RootElement.GetProperty("rt").GetString());
        Assert.Equal("after failure", after.RootElement.GetProperty("rt").GetString());
        Assert.Equal("next phrase", next.RootElement.GetProperty("rt").GetString());
    }

    [Fact]
    public void Add_CompleteTail_DoesNotAddAnEmptyLine()
    {
        Directory.CreateDirectory(_paths.MemoryDirectory);
        const string previous = "{\"rt\":\"old complete\"}\n";
        File.WriteAllText(HistoryFile(), previous, new System.Text.UTF8Encoding(false));

        Add(CreateMemory(), "next phrase");

        string[] lines = File.ReadAllLines(HistoryFile());
        Assert.Equal(2, lines.Length);
        Assert.Equal(previous.TrimEnd('\n'), lines[0]);
        using JsonDocument next = JsonDocument.Parse(lines[1]);
        Assert.Equal("next phrase", next.RootElement.GetProperty("rt").GetString());
    }

    [Fact]
    public void Add_TurnedOff_CreatesNoFolder()
    {
        // Act
        Add(CreateMemory(enabled: false), "private phrase");

        // Assert
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void Add_NewMonth_StartsAnotherFileAndKeepsThePreviousOne()
    {
        // Arrange
        DictationMemory memory = CreateMemory();
        Add(memory, "October");
        _time.Setup(clock => clock.GetUtcNow()).Returns(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));

        // Act
        Add(memory, "November");

        // Assert
        Assert.Contains("October", File.ReadAllText(HistoryFile()));
        Assert.Contains("November", File.ReadAllText(Path.Combine(_paths.MemoryDirectory, "2026-11.jsonl")));
        Assert.Equal(2, Directory.GetFiles(_paths.MemoryDirectory).Length);
    }

    [Fact]
    public void Add_NewMonth_UsesLocalTimeForTheFileAndTimestamp()
    {
        // Arrange: still October in UTC, already November in the user's time zone
        _time.Setup(clock => clock.LocalTimeZone).Returns(TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(4), "test", "test"));
        _time.Setup(clock => clock.GetUtcNow()).Returns(new DateTimeOffset(2026, 10, 31, 22, 0, 0, TimeSpan.Zero));

        // Act
        Add(CreateMemory(), "local month");

        // Assert
        string file = Path.Combine(_paths.MemoryDirectory, "2026-11.jsonl");
        using JsonDocument entry = JsonDocument.Parse(File.ReadAllText(file));

        Assert.Equal("2026-11-01T02:00:00", entry.RootElement.GetProperty("ts").GetString());
        Assert.False(File.Exists(HistoryFile()));
    }

    [Fact]
    public void Add_FolderIsAFile_DoesNotThrowAndCanBeRetried()
    {
        // Arrange
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_paths.MemoryDirectory, "not a folder");
        DictationMemory memory = CreateMemory();

        // Act
        Exception? error = Record.Exception(() => Add(memory, "cannot be kept"));
        File.Delete(_paths.MemoryDirectory);
        Add(memory, "can be kept now");

        // Assert
        Assert.Null(error);
        Assert.Contains("can be kept now", Assert.Single(File.ReadAllLines(HistoryFile())));
    }

    [Fact]
    public void Add_LargeExistingHistory_DoesNotLoadItIntoMemoryOrReplaceIt()
    {
        // Arrange: deliberately not valid JSON; appending must never parse an existing history
        DictationMemory memory = CreateMemory();
        Add(memory, "first phrase");
        long length = 8 * 1024 * 1024;

        using (var file = new FileStream(HistoryFile(), FileMode.Open, FileAccess.Write))
            file.SetLength(length);

        long before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        Add(memory, "last phrase");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert: history size has no bearing on the allocation for the next phrase
        Assert.InRange(allocated, 0, 256 * 1024);
        Assert.True(new FileInfo(HistoryFile()).Length > length);
        using var reading = new StreamReader(HistoryFile());
        Assert.Contains("first phrase", reading.ReadLine());
        reading.BaseStream.Seek(length, SeekOrigin.Begin);
        reading.DiscardBufferedData();
        Assert.Contains("last phrase", reading.ReadToEnd());
    }

    [Fact]
    public void Add_FileStaysBusy_GivesUpAndTheNextPhraseCanBeKept()
    {
        // Arrange
        DictationMemory memory = CreateMemory();
        Add(memory, "before");
        var waited = System.Diagnostics.Stopwatch.StartNew();

        // Act: another process, or a reader, holds the file beyond the bounded wait
        using (var held = new FileStream(HistoryFile(), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Add(memory, "busy");

        waited.Stop();
        Add(memory, "after");

        // Assert
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(5));
        string[] lines = File.ReadAllLines(HistoryFile());
        Assert.Equal(2, lines.Length);
        Assert.Contains("before", lines[0]);
        Assert.Contains("after", lines[1]);
    }

    [Fact]
    public async Task Add_IndependentWritersAtOnce_KeepEveryWholeLine()
    {
        // Arrange: separate services, just as separate instances of Echo would have
        DictationMemory first = CreateMemory();
        DictationMemory second = CreateMemory();
        using var start = new Barrier(2);

        // Act
        await Task.WhenAll(
            Task.Run(() => { start.SignalAndWait(); for (int i = 0; i < 100; i++) Add(first, $"first {i}"); }),
            Task.Run(() => { start.SignalAndWait(); for (int i = 0; i < 100; i++) Add(second, $"second {i}"); }));

        // Assert
        string[] lines = File.ReadAllLines(HistoryFile());
        Assert.Equal(200, lines.Length);
        string[] phrases = [.. lines.Select(line =>
        {
            using JsonDocument entry = JsonDocument.Parse(line);
            return entry.RootElement.GetProperty("rt").GetString()!;
        })];
        Assert.Equal(200, phrases.Distinct().Count());
    }

    /// <summary>
    ///     Makes a writer with the same paths and independent service state.
    /// </summary>
    private DictationMemory CreateMemory(bool enabled = true) => new(
        _paths,
        Options.Create(new MemorySettings { Enabled = enabled }),
        Options.Create(new WhisperSettings { ModelName = "ggml-large-v3", Language = "ru", Translate = true }),
        Options.Create(new RecordingDeviceSettings { SilenceThreshold = 0.03f }),
        _time.Object,
        Mock.Of<IRecognitionRuntime>(runtime => runtime.Loaded == Whisper.net.LibraryLoader.RuntimeLibrary.Cuda),
        new WhisperSettings());

    /// <summary>
    ///     Adds a phrase without overriding the test's settings or clock.
    /// </summary>
    private static void Add(IDictationMemory memory, string text) =>
        memory.Add(text, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

    /// <summary>
    ///     The file of the initial month.
    /// </summary>
    private string HistoryFile() => Path.Combine(_paths.MemoryDirectory, "2026-10.jsonl");
}