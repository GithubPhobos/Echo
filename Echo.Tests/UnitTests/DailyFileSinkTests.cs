using Echo.Listening;
using Serilog.Events;
using Serilog.Parsing;
using Spectre.Console;

namespace Echo.Tests.UnitTests;

public sealed class DailyFileSinkTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    public DailyFileSinkTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static readonly DateTimeOffset _time = new(new DateTime(2026, 10, 14, 21, 3, 0, DateTimeKind.Local));

    [Fact]
    public void Emit_WritesTheDayIntoItsFileAndKeepsOnlyTheLatestDays()
    {
        // Arrange: older files than are kept
        Directory.CreateDirectory(_directory);

        for (int day = 1; day <= 9; day++)
            File.WriteAllText(Path.Combine(_directory, DailyFileSink.FileName(_time.AddDays(-day))), "old");

        // Act
        using (var sink = new DailyFileSink(_directory))
            sink.Emit(Event(LogEventLevel.Information, "{Tab}Push-to-talk pressed", ("Tab", "    ")));

        // Assert
        string[] files = [.. Directory.GetFiles(_directory).Select(Path.GetFileName).Order()!];

        Assert.Equal(DailyFileSink.KeptDays, files.Length);
        Assert.Equal(DailyFileSink.FileName(_time), files[^1]);
        Assert.EndsWith("[INF]     Push-to-talk pressed", File.ReadAllText(Path.Combine(_directory, files[^1])).TrimEnd());
    }

    private static LogEvent Event(LogEventLevel level, string template, params (string Name, object Value)[] properties) =>
        new(
            _time,
            level,
            exception: null,
            new MessageTemplateParser().Parse(template),
            properties.Select(property => new LogEventProperty(property.Name, new ScalarValue(property.Value))));
}