using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace Echo.Listening;

/// <summary>
///     Shows log messages on the working screen.
/// </summary>
/// <param name="log">The screen's log.</param>
internal sealed class ScreenLogSink(ScreenLog log) : ILogEventSink
{
    /// <summary>
    ///     The property that keeps a message off the screen while it still goes to the file: the screen
    ///     says the same thing in its own way, such as a recognized phrase.
    /// </summary>
    public const string HiddenProperty = "HiddenOnScreen";

    private readonly ScreenLog _log = log;

    /// <inheritdoc/>
    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Properties.ContainsKey(HiddenProperty))
            return;

        string message = LogText.Message(logEvent);
        string text = Clean(message);

        if (logEvent.Exception is { } exception && !text.Contains(exception.Message, StringComparison.Ordinal))
            text = $"{text} {exception.Message}";

        // Lines that are only decoration, such as a row of "=", say nothing on the screen
        if (text.Length == 0 || text.All(character => !char.IsLetterOrDigit(character)))
            return;

        EntryKind kind = logEvent.Level switch
        {
            >= LogEventLevel.Error => EntryKind.Error,
            LogEventLevel.Warning => EntryKind.Warning,
            _ => EntryKind.Message
        };

        int depth = DepthOf(message);

        _log.Add(new ScreenEntry(logEvent.Timestamp, kind, text)
        {
            Depth = depth,
            Tone = ToneOf(message.TrimStart(), text, depth, logEvent.Level),
        });
    }

    /// <summary>
    ///     How many steps of indentation the message starts with: the services indent the steps of a group
    ///     by <see cref="LoggerConstants.Tab"/>.
    /// </summary>
    public static int DepthOf(string message)
    {
        int spaces = message.Length - message.TrimStart(' ').Length;

        return Math.Min(2, spaces / LoggerConstants.Tab.Length);
    }

    /// <summary>
    ///     What the message says about how things went, read from the picture it starts with.
    /// </summary>
    public static MessageTone ToneOf(string message, string text, int depth, LogEventLevel level)
    {
        if (message.Contains(LoggerConstants.EarEmoji, StringComparison.Ordinal))
            return MessageTone.Title;

        if (message.StartsWith(LoggerConstants.CheckedEmoji, StringComparison.Ordinal))
            return MessageTone.Done;

        if (message.StartsWith(LoggerConstants.CrossEmoji, StringComparison.Ordinal))
            return MessageTone.Failed;

        if (message.StartsWith(LoggerConstants.LoadingEmoji, StringComparison.Ordinal)
            || message.StartsWith(LoggerConstants.HourglassEmoji, StringComparison.Ordinal))
        {
            return MessageTone.Progress;
        }

        if (message.StartsWith(LoggerConstants.BlackSquareButtonEmoji, StringComparison.Ordinal)
            || message.StartsWith(LoggerConstants.WhiteSquareButtonEmoji, StringComparison.Ordinal))
        {
            return MessageTone.Key;
        }

        // A line of its own that ends with a colon names the group of steps that follows it
        return depth == 0 && level <= LogEventLevel.Information && text.EndsWith(':')
            ? MessageTone.Heading
            : MessageTone.Plain;
    }

    /// <summary>
    ///     The message without the indentation and the pictures it starts with: on the screen the level of
    ///     a message is shown by its color, and the console the messages were written for is gone.
    /// </summary>
    public static string Clean(string message)
    {
        string text = message.Trim();
        int start = 0;

        while (start < text.Length)
        {
            if (char.IsWhiteSpace(text[start]) || IsPicture(text, start))
            {
                start += char.IsSurrogatePair(text, start) ? 2 : 1;

                continue;
            }

            break;
        }

        // The ear in "Ech👂" stands for the "o" of the name
        return text[start..].TrimEnd().Replace("Ech" + LoggerConstants.EarEmoji, "Echo", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Whether the character at <paramref name="index"/> is part of an emoji or a symbol drawn as a picture.
    /// </summary>
    private static bool IsPicture(string text, int index)
    {
        char character = text[index];

        // Joiners, the selector that asks for the picture form of the symbol before them,
        // and "information source", which Unicode files as a letter
        if (character is '‍' or '️' or '︎' or '⃣' or 'ℹ')
            return true;

        UnicodeCategory category = char.IsSurrogatePair(text, index)
            ? CharUnicodeInfo.GetUnicodeCategory(char.ConvertToUtf32(text, index))
            : CharUnicodeInfo.GetUnicodeCategory(character);

        return category is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.MathSymbol
            || (char.IsSurrogate(character) && category is UnicodeCategory.OtherNotAssigned);
    }
}