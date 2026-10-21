namespace Echo.Tests.UnitTests;

/// <summary>
///     A settings file kept in memory.
/// </summary>
internal sealed class MemorySettings : IUserSettingsStore
{
    private readonly Dictionary<string, string?> _values = [];

    public UserSettings Load() => new();

    public void Save(UserSettings settings)
    {
    }

    public string? ReadValue(string section, string key)
    {
        lock (_values)
            return _values.GetValueOrDefault($"{section}:{key}");
    }

    public void WriteValue(string section, string key, string? value)
    {
        lock (_values)
            _values[$"{section}:{key}"] = value;
    }
}