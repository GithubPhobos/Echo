namespace Echo.Services;

/// <inheritdoc cref="IAssetsProvider"/>
/// <param name="logger"><see cref="ILogger{AssetsProvider}"/></param>
internal sealed class AssetsProvider(ILogger<AssetsProvider> logger) : IAssetsProvider
{
    private const string AssetsDirName = "Assets";

    private readonly ILogger<AssetsProvider> _logger = logger;

    private string _assetsPath = null!;

    /// <inheritdoc/>
    public void InitializeAssetsDirectory()
    {
        _logger.LogDebug("{FolderEmoji} Assets:", LoggerConstants.FolderEmoji);

        var currentDir = Directory.GetCurrentDirectory();

        _assetsPath = Path.Combine(currentDir, AssetsDirName);

        _logger.LogDebug("{Tab}{LoadingEmoji} Checking if 'Assets' folder exists...", 
            LoggerConstants.Tab, LoggerConstants.LoadingEmoji);

        if (!Directory.Exists(_assetsPath))
        {
            _logger.LogDebug("{DoubleTab}{HourglassEmoji} 'Assets' folder not found, creating...",
                LoggerConstants.DoubleTab, LoggerConstants.HourglassEmoji);

            try
            {
                Directory.CreateDirectory(_assetsPath);
            }
            catch (Exception exc)
            {
                _logger.LogDebug("{DoubleTab}Error creating 'Assets' folder: {ErrorMessage}.", 
                    LoggerConstants.DoubleTab, exc.Message);

                return;
            }
        }

        _logger.LogDebug("{Tab}{CheckedEmoji} 'Assets' folder ready.",
            LoggerConstants.Tab, LoggerConstants.CheckedEmoji);
    }

    /// <inheritdoc/>
    public string GetFilePath(string fileName)
    {
        try
        {
            // 'Path Traversal' protection
            var fullPath = Path.GetFullPath(Path.Combine(_assetsPath, fileName));
            if (!fullPath.StartsWith(_assetsPath))
            {
                throw new ArgumentException(
                    $"{LoggerConstants.ExplosionEmoji} Invalid file name: {fileName}.");
            }

            return fullPath;
        }
        catch
        {
            throw;
        }
    }

    /// <inheritdoc/>
    public bool CheckIfExists(string filePath) =>
        Path.Exists(filePath);
}