namespace Echo.Listening;

/// <summary>
///     A row of the panel under the command line: a command while one is typed, or a value to choose.
/// </summary>
/// <param name="Text">What the command line becomes when the row is taken.</param>
/// <param name="Label">What the row is called.</param>
/// <param name="Description">What it does.</param>
/// <param name="IsCurrent">Whether it is the value in effect now.</param>
internal sealed record PanelRow(string Text, string Label, string Description, bool IsCurrent = false);