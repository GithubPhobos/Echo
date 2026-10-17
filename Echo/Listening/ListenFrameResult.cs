namespace Echo.Listening;

/// <summary>
///     A drawn working screen: one markup string per row of the terminal.
/// </summary>
/// <param name="Lines">The rows from the top.</param>
/// <param name="LogRows">How many rows the log has on this screen.</param>
/// <param name="ScrollRows">How far back the log is scrolled, after keeping it within what there is.</param>
internal sealed record ListenFrameResult(IReadOnlyList<string> Lines, int LogRows, int ScrollRows);