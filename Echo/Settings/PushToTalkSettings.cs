namespace Echo.Settings;

/// <summary>
///     Push to talk settings.
/// </summary>
internal sealed class PushToTalkSettings
{
    /// <summary>
    ///     Main push to talk key.
    /// </summary>
    public string Key { get; init; } = null!;

    /// <summary>
    ///     Push to talk modifier key.
    /// </summary>
    public string? Modifier { get; init; }

    /// <summary>
    ///     Indicates whether sound will be played on push to talk key presses.
    /// </summary>
    public bool PlaySound { get; init; }

    /// <summary>
    ///     Push to talk sound volume.
    /// </summary>
    public float? Volume { get; init; }
}