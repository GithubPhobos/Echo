namespace Echo.Platform;

/// <summary>
///     One thing Echo needs from the system, in the current interface language.
/// </summary>
/// <param name="Name">What it is.</param>
/// <param name="IsMet">Whether the system has it.</param>
/// <param name="Consequence">What does not work without it.</param>
/// <param name="Fix">How to get it, such as the command that installs it; null when there is nothing to install.</param>
internal sealed record SystemRequirement(string Name, bool IsMet, string Consequence, string? Fix);