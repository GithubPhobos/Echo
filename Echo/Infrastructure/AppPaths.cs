namespace Echo.Infrastructure;

/// <inheritdoc cref="IAppPaths"/>
/// <param name="configRoot">The directory for settings.</param>
/// <param name="dataRoot">The directory for downloaded files.</param>
/// <param name="stateRoot">The directory for the log; the one for settings when not given.</param>
internal sealed class AppPaths(string configRoot, string dataRoot, string? stateRoot = null) : IAppPaths
{
    /// <summary>
    ///     Environment variable that moves all application files into a single directory.
    /// </summary>
    public const string HomeVariable = "ECHOOO_HOME";

    /// <inheritdoc/>
    public string SettingsFile { get; } = Path.Combine(configRoot, "settings.json");

    /// <inheritdoc/>
    public string ModelsDirectory { get; } = Path.Combine(dataRoot, "models");

    /// <inheritdoc/>
    public string CudaDirectory { get; } = Path.Combine(dataRoot, "runtimes", "cuda");

    public string LogsDirectory { get; } = Path.Combine(stateRoot ?? configRoot, "logs");

    /// <inheritdoc/>
    public string MemoryDirectory { get; } = Path.Combine(dataRoot, "memory");

    /// <summary>
    ///     Resolves the per-user directories of the current operating system.
    /// </summary>
    public static AppPaths CreateDefault()
    {
        string? home = Environment.GetEnvironmentVariable(HomeVariable);
        if (!string.IsNullOrWhiteSpace(home))
            return new AppPaths(home, home);

        if (OperatingSystem.IsLinux())
            return ForLinux(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        return new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationInfo.CommandName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationInfo.CommandName));
    }

    /// <summary>
    ///     The folders the XDG standard gives every Linux program: settings in '~/.config', downloaded files in
    ///     '~/.local/share' and the log in '~/.local/state', each moved elsewhere by its own variable.
    /// </summary>
    /// <param name="variable">Reads an environment variable.</param>
    /// <param name="userHome">The home folder of the user.</param>
    internal static AppPaths ForLinux(Func<string, string?> variable, string userHome) => new(
        Path.Combine(XdgDirectory(variable, "XDG_CONFIG_HOME", userHome, ".config"), ApplicationInfo.CommandName),
        Path.Combine(XdgDirectory(variable, "XDG_DATA_HOME", userHome, ".local", "share"), ApplicationInfo.CommandName),
        Path.Combine(XdgDirectory(variable, "XDG_STATE_HOME", userHome, ".local", "state"), ApplicationInfo.CommandName));

    /// <summary>
    ///     The folder an XDG variable names. The standard says to ignore a value that is empty or not a full
    ///     path, and to use the default folder instead.
    /// </summary>
    internal static string XdgDirectory(Func<string, string?> variable, string name, string userHome, params string[] fallback)
    {
        string? value = variable(name);

        return !string.IsNullOrEmpty(value) && Path.IsPathRooted(value)
            ? value
            : Path.Combine([userHome, .. fallback]);
    }
}