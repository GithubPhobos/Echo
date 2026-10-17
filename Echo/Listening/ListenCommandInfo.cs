namespace Echo.Listening;

/// <summary>
///     A command of the working screen.
/// </summary>
/// <param name="Name">What is typed, with its slash.</param>
/// <param name="Description">What it does, in the interface language.</param>
/// <param name="Arguments">The words that may follow it, if any; such a command is chosen from a list.</param>
internal sealed record ListenCommandInfo(string Name, Func<string> Description, IReadOnlyList<string>? Arguments = null);