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
}