namespace Echo.UI;

/// <summary>
///     A key that picks the highlighted row of a list.
/// </summary>
/// <param name="Key">The key.</param>
/// <param name="Hint">What the line of key hints says it does; null for a key that works without being named there.</param>
internal readonly record struct ListKey(ConsoleKey Key, string? Hint);