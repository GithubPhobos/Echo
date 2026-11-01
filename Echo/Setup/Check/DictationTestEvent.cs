namespace Echo.Setup.Check;

/// <summary>
///     One event of a test dictation.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Text">The recognized text, or the error message.</param>
/// <param name="ElapsedMs">How long recognition took.</param>
internal sealed record DictationTestEvent(DictationTestEventKind Kind, string? Text = null, double ElapsedMs = 0);