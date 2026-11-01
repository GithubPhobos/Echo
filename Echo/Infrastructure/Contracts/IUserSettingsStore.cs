namespace Echo.Infrastructure.Contracts;

/// <summary>
///     Reads and writes the user settings file.
/// </summary>
internal interface IUserSettingsStore
{
    /// <summary>
    ///     Reads the saved settings. Returns empty settings when the file is missing or unreadable.
    /// </summary>
    UserSettings Load();

    /// <summary>
    ///     Saves the settings, keeping everything else the user has put into the file.
    /// </summary>
    void Save(UserSettings settings);

    /// <summary>
    ///     Reads one value that is kept outside the choices of the wizard, such as a permission the desktop gave.
    /// </summary>
    /// <param name="section">The section of the file; sections within sections are separated by a colon.</param>
    /// <param name="key">The name of the value.</param>
    string? ReadValue(string section, string key);

    /// <summary>
    ///     Saves one value, keeping everything else in the file; no value removes it.
    /// </summary>
    /// <param name="section">The section of the file; sections within sections are separated by a colon.</param>
    /// <param name="key">The name of the value.</param>
    /// <param name="value">The value.</param>
    void WriteValue(string section, string key, string? value);
}