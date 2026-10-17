namespace Echo.Infrastructure;

/// <inheritdoc cref="IAppPaths"/>
/// <param name="configRoot">The directory for settings.</param>
/// <param name="dataRoot">The directory for downloaded files.</param>
internal sealed class AppPaths(string configRoot, string dataRoot) : IAppPaths
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

    public string LogsDirectory { get; } = Path.Combine(configRoot, "logs");

    /// <summary>
    ///     Resolves the per-user directories of the current operating system.
    /// </summary>
    public static AppPaths CreateDefault()
    {
        string? home = Environment.GetEnvironmentVariable(HomeVariable);
        if (!string.IsNullOrWhiteSpace(home))
            return new AppPaths(home, home);

        return new AppPaths(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationInfo.CommandName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationInfo.CommandName));
    }
}