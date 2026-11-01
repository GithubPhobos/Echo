namespace Echo.Services;

/// <inheritdoc cref="IAssetsProvider"/>
internal sealed class AssetsProvider : IAssetsProvider
{
    private const string AssetsDirName = "Assets";

    /// <summary>
    ///     Assets ship next to the executable, wherever the command is run from. They are only read: a missing
    ///     sound is reported by the sounds themselves.
    /// </summary>
    private readonly string _assetsPath = Path.Combine(AppContext.BaseDirectory, AssetsDirName);

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