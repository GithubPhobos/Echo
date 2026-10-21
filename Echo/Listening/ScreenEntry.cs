namespace Echo.Listening;

/// <summary>
///     One line of the working screen's log.
/// </summary>
/// <param name="Time">When it happened.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Text">The text itself, plain.</param>
/// <param name="Note">A few words shown faintly after the text, such as how long recognition took.</param>
/// <param name="Indent">
///     How much further than the text its continuation rows start, so that a long value of a table row
///     stays in its column.
/// </param>
internal sealed record ScreenEntry(DateTimeOffset Time, EntryKind Kind, string Text, string? Note = null, int Indent = 0)
{
    /// <summary>
    ///     How deep in a group the line is: 0 for a heading or a line of its own, 1 for a step of a group,
    ///     2 for a step of a step.
    /// </summary>
    public int Depth { get; init; }

    public MessageTone Tone { get; init; }

    /// <summary>
    ///     Parts of the text drawn in a style of their own, such as the name of a log level in the color of that
    ///     level: where each part starts in the text, how long it is, and its console style.
    /// </summary>
    public IReadOnlyList<(int Start, int Length, string Style)> Accents { get; init; } = [];
}