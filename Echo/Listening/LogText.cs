using System.Globalization;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Echo.Listening;

/// <summary>
///     The text of a log message.
/// </summary>
internal static class LogText
{
    // Text values are written as they are, not in quotes: the messages were written for a console that shows them so
    private static readonly MessageTemplateTextFormatter _formatter = new("{Message:lj}", CultureInfo.InvariantCulture);

    public static string Message(LogEvent logEvent)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);

        _formatter.Format(logEvent, writer);

        return writer.ToString();
    }
}