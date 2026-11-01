namespace Echo.Setup;

/// <summary>
///     One setting of the "Additional settings" step.
/// </summary>
/// <param name="Id">
///     Names the setting in code and in the interface texts: "Additional{Id}" is its label
///     and "Additional{Id}Hint" its explanation.
/// </param>
/// <param name="Group">The block of the list it is shown in.</param>
/// <param name="Section">The section of the settings file it lives in.</param>
/// <param name="Key">Its key within that section.</param>
/// <param name="Kind">How it is shown and edited.</param>
/// <param name="Default">
///     The value that ships with the program, written the way <see cref="AdditionalValues"/> keeps values;
///     null when the program ships without one.
/// </param>
/// <param name="Min">The lowest number accepted, for numeric kinds.</param>
/// <param name="Max">The highest number accepted, for numeric kinds.</param>
internal sealed record AdditionalSetting(
    string Id,
    SettingGroup Group,
    string Section,
    string Key,
    SettingKind Kind,
    string? Default,
    double Min = 0,
    double Max = 0)
{
    /// <summary>
    ///     The values the setting steps through, written the way <see cref="AdditionalValues"/> keeps them.
    ///     For a fixed set of values these are all of them; for a number, the ones worth trying,
    ///     while any other number in the range can still be typed.
    /// </summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    /// <summary>
    ///     Whether there is something about the setting that must not be overlooked.
    /// </summary>
    public bool HasCaution { get; init; }

    /// <summary>
    ///     Whether the setting can take <paramref name="value"/>, written the way <see cref="AdditionalValues"/>
    ///     keeps values: a switch is on or off, a number lies within its range, a choice is one of its steps.
    /// </summary>
    public bool Accepts(string value) => Kind switch
    {
        SettingKind.Switch => value is AdditionalSettings.On or AdditionalSettings.Off,
        SettingKind.Whole => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int whole)
            && whole >= Min && whole <= Max,
        SettingKind.Fraction or SettingKind.Percent => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            && number >= Min && number <= Max,
        SettingKind.Choice => Steps.Contains(value),
        _ => true
    };

    public string Label => Strings.Get($"Additional{Id}");

    public string Hint => Strings.Get($"Additional{Id}Hint");

    /// <summary>
    ///     What must not be overlooked about the setting, or null. Its text is "Additional{Id}Caution".
    /// </summary>
    public string? Caution => HasCaution ? Strings.Get($"Additional{Id}Caution") : null;
}