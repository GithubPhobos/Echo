using Echo.Listening;
using Serilog.Events;
using Serilog.Parsing;
using Spectre.Console;

namespace Echo.Tests.UnitTests;

public sealed class ScreenLogSinkTests
{
    private static readonly DateTimeOffset _time = new(new DateTime(2026, 10, 14, 21, 3, 0, DateTimeKind.Local));

    [Fact]
    public void Emit_ShowsMessagesWithoutTheirPicturesAndLeavesOutHiddenOnes()
    {
        // Arrange
        var log = new ScreenLog();
        var sink = new ScreenLogSink(log);

        // Act: written the way the services write them, with indentation and emoji in properties
        sink.Emit(Event(LogEventLevel.Debug, "{Tab}{CheckedEmoji} Whisper model loaded.", ("Tab", "    "), ("CheckedEmoji", "✅")));
        sink.Emit(Event(LogEventLevel.Warning, "{Emoji} Audio was silent or empty!", ("Emoji", "⚠️")));
        sink.Emit(Event(LogEventLevel.Debug, "{Tab}{Emoji} Current mic amplitude", ("Tab", "    "), ("Emoji", "ℹ️")));
        sink.Emit(Event(LogEventLevel.Debug, "========================================"));
        sink.Emit(Event(LogEventLevel.Debug, "Final result: hello", (ScreenLogSink.HiddenProperty, true)));

        // Assert
        Assert.Equal(
            [(EntryKind.Message, "Whisper model loaded."), (EntryKind.Warning, "Audio was silent or empty!"), (EntryKind.Message, "Current mic amplitude")],
            log.Snapshot().Select(entry => (entry.Kind, entry.Text)));
    }

    [Fact]
    public void Emit_LineOfTheRecognitionLibrary_IsMarkedAsSuch()
    {
        // Arrange
        var log = new ScreenLog();
        var sink = new ScreenLogSink(log);

        // Act: a line the library writes about the graphics card, and one of Echo's own next to it
        sink.Emit(Event(LogEventLevel.Debug, "{Tab}{Message}", ("Tab", "    "), ("Message", "whisper_backend_init_gpu: using CUDA0 backend"), (ScreenLogSink.LibraryProperty, true)));
        sink.Emit(Event(LogEventLevel.Debug, "{Tab}{Emoji} Whisper model loaded.", ("Tab", "    "), ("Emoji", "✅")));

        // Assert
        Assert.Equal(
            [(MessageTone.Library, "whisper_backend_init_gpu: using CUDA0 backend"), (MessageTone.Done, "Whisper model loaded.")],
            log.Snapshot().Select(entry => (entry.Tone, entry.Text)));
    }

    [Fact]
    public void Emit_ReadsGroupsAndOutcomesFromTheIndentationAndThePictures()
    {
        // Arrange
        var log = new ScreenLog();
        var sink = new ScreenLogSink(log);

        // Act: a start the way the services of 1.0 write it
        sink.Emit(Event(LogEventLevel.Information, "Ech{Ear} {Version} is checking dependencies...", ("Ear", "👂"), ("Version", "2.0.0")));
        sink.Emit(Event(LogEventLevel.Debug, "{Emoji} Audio:", ("Emoji", "🎛️")));
        sink.Emit(Event(LogEventLevel.Debug, "{Tab}{Emoji} Analyzing audio devices...", ("Tab", "    "), ("Emoji", "🔄")));
        sink.Emit(Event(LogEventLevel.Debug, "{Tab}Found 1 recording devices:", ("Tab", "    ")));
        sink.Emit(Event(LogEventLevel.Debug, "{Tab}{Emoji} Successfuly analized audio devices.", ("Tab", "    "), ("Emoji", "✅")));
        sink.Emit(Event(LogEventLevel.Debug, "{Tab}{Emoji} Modifier key was not set up.", ("Tab", "    "), ("Emoji", "❌")));
        sink.Emit(Event(LogEventLevel.Information, "{Emoji} Push-to-talk pressed.", ("Emoji", "🔲")));

        // Assert
        Assert.Equal(
            [
                (0, MessageTone.Title, "Echo 2.0.0 is checking dependencies..."),
                (0, MessageTone.Heading, "Audio:"),
                (1, MessageTone.Progress, "Analyzing audio devices..."),
                (1, MessageTone.Plain, "Found 1 recording devices:"),
                (1, MessageTone.Done, "Successfuly analized audio devices."),
                (1, MessageTone.Failed, "Modifier key was not set up."),
                (0, MessageTone.Key, "Push-to-talk pressed."),
            ],
            log.Snapshot().Select(entry => (entry.Depth, entry.Tone, entry.Text)));
    }

    private static LogEvent Event(LogEventLevel level, string template, params (string Name, object Value)[] properties) =>
        new(
            _time,
            level,
            exception: null,
            new MessageTemplateParser().Parse(template),
            properties.Select(property => new LogEventProperty(property.Name, new ScalarValue(property.Value))));
}