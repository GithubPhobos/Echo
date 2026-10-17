using System.Globalization;

namespace Echo.Setup;

/// <summary>
///     The values of the additional settings the user has set. Every value is kept as text written
///     the same way in any interface language: "true" and "false", "50", "0.1", or the text itself.
///     A setting without a value falls back to the one that ships with the program.
/// </summary>
internal sealed class AdditionalValues : IEquatable<AdditionalValues>
{
    private readonly SortedDictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>
    ///     The settings that have a value of their own.
    /// </summary>
    public IEnumerable<AdditionalSetting> Changed =>
        AdditionalSettings.All.Where(setting => Stored(setting) is { } value && value != setting.Default);

    /// <summary>
    ///     What the user has set, or null when the setting was left alone.
    /// </summary>
    public string? Stored(AdditionalSetting setting) =>
        _values.GetValueOrDefault(setting.Id);

    /// <summary>
    ///     The value in effect: the user's own, or the one that ships with the program.
    /// </summary>
    public string? Get(AdditionalSetting setting) =>
        Stored(setting) ?? setting.Default;

    public bool IsOn(AdditionalSetting setting) =>
        Get(setting) == AdditionalSettings.On;

    /// <summary>
    ///     Sets a value. An empty one, or the one the program ships with, means "left alone":
    ///     nothing is kept, so a later change of the shipped value reaches the user.
    /// </summary>
    public void Set(AdditionalSetting setting, string? value)
    {
        if (string.IsNullOrEmpty(value) || value == setting.Default)
            _values.Remove(setting.Id);
        else
            _values[setting.Id] = value;
    }

    public void Switch(AdditionalSetting setting) =>
        Set(setting, IsOn(setting) ? AdditionalSettings.Off : AdditionalSettings.On);

    /// <summary>
    ///     Moves a setting to the next of its <see cref="AdditionalSetting.Steps"/>, and round again after
    ///     the last one. A setting that ships without a value passes through "no value" on the way round.
    ///     A switch is flipped; a setting without steps is left as it is.
    /// </summary>
    public void Step(AdditionalSetting setting)
    {
        if (setting.Kind == SettingKind.Switch)
        {
            Switch(setting);

            return;
        }

        if (setting.Steps.Count == 0)
            return;

        if (Get(setting) is not { } value)
        {
            Set(setting, setting.Steps[0]);

            return;
        }

        int index = NextStep(setting, value);

        // Past the last step: back to "no value" where there is one, otherwise to the first step
        if (index < setting.Steps.Count)
            Set(setting, setting.Steps[index]);
        else
            Set(setting, setting.Default is null ? null : setting.Steps[0]);
    }

    /// <summary>
    ///     The step after <paramref name="value"/>. A number typed between two steps moves on to the higher one.
    /// </summary>
    private static int NextStep(AdditionalSetting setting, string value)
    {
        if (setting.Kind == SettingKind.Choice)
            return setting.Steps.ToList().IndexOf(value) + 1;

        double number = double.Parse(value, CultureInfo.InvariantCulture);

        int index = 0;

        while (index < setting.Steps.Count && double.Parse(setting.Steps[index], CultureInfo.InvariantCulture) <= number)
            index++;

        return index;
    }

    /// <summary>
    ///     Keeps a value exactly as it was read from the settings file, even when it repeats the shipped one.
    /// </summary>
    public AdditionalValues With(AdditionalSetting setting, string? value)
    {
        AdditionalValues copy = Clone();

        if (string.IsNullOrEmpty(value))
            copy._values.Remove(setting.Id);
        else
            copy._values[setting.Id] = value;

        return copy;
    }

    public AdditionalValues Clone()
    {
        var copy = new AdditionalValues();

        foreach ((string id, string value) in _values)
            copy._values[id] = value;

        return copy;
    }

    /// <inheritdoc/>
    public bool Equals(AdditionalValues? other) =>
        other is not null && _values.SequenceEqual(other._values);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        Equals(obj as AdditionalValues);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach ((string id, string value) in _values)
        {
            hash.Add(id);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}