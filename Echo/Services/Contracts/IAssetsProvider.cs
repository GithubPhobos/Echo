namespace Echo.Services.Contracts;

/// <summary>
///     Manages the application's external assets, such as audio files and AI models.
/// </summary>
internal interface IAssetsProvider
{
    /// <summary>
    ///     Creates the 'Assets' directory in the application's root if it does not already exist.
    /// </summary>
    public void InitializeAssetsDirectory();

    /// <summary>
    ///     Constructs a secure absolute path for a given file name within the 'Assets' directory.
    /// </summary>
    /// <param name="fileName">The name of the file to locate.</param>
    /// <returns>The fully qualified path to the asset.</returns>
    /// <exception cref="ArgumentException">Thrown when a Path Traversal attack is detected (e.g., using "../").</exception>
    public string GetFilePath(string fileName);

    /// <summary>
    ///     Check whether file exists.
    /// </summary>
    /// <param name="filePath">File path.</param>
    /// <returns><see cref="bool"/></returns>
    public bool CheckIfExists(string filePath);
}
