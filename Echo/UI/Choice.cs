namespace Echo.UI;

/// <summary>
///     One row of a selection list.
/// </summary>
/// <param name="Value">What is returned when the row is chosen.</param>
/// <param name="Label">The main text.</param>
/// <param name="Summary">
///     A few words shown on the same line, to the right of the label: a size, a mark, the current value.
///     It is console markup, so parts of it can be colored.
/// </param>
/// <param name="Hint">
///     What choosing it means, in a sentence or two. Shown under the list while the row is highlighted.
/// </param>
internal sealed record Choice<T>(T Value, string Label, string? Summary = null, string? Hint = null)
{
    /// <summary>
    ///     More console markup for the same line, in a column of its own to the right of the summary.
    ///     In a list that has remarks the summaries line up by their right edge, as a column of figures does.
    /// </summary>
    public string? Remark { get; init; }

    /// <summary>
    ///     Whether an empty line sets the row apart from the rows above it. Such a row starts a list
    ///     of its own: however long its label is, the summaries above stay next to their labels.
    /// </summary>
    public bool IsSetApart { get; init; }

    /// <summary>
    ///     The name of the block the row belongs to. Rows that follow each other with the same name
    ///     are shown under one heading.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    ///     Something about the choice the user should not overlook. Shown on a line of its own
    ///     under the explanation, in the color of a warning.
    /// </summary>
    public string? Caution { get; init; }
}