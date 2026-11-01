namespace Echo.Setup.Hotkeys;

/// <summary>
///     A push-to-talk key as it is stored in the settings.
/// </summary>
/// <param name="Key">The name of the key, as the keyboard hook reports it.</param>
/// <param name="Modifier">The name of the modifier held together with the key, or an empty string.</param>
internal sealed record HotkeyChoice(string Key, string Modifier)
{
    /// <summary>
    ///     The key used until the user picks another one.
    /// </summary>
    public static HotkeyChoice Default { get; } = new(nameof(KeyCode.VcBackQuote), string.Empty);
}