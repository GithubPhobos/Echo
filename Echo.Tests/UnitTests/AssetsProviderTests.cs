namespace Echo.Tests.UnitTests;

public sealed class AssetsProviderTests
{
    private readonly AssetsProvider _provider;

    public AssetsProviderTests()
    {
        var loggerMock = new Mock<ILogger<AssetsProvider>>();

        _provider = new AssetsProvider(loggerMock.Object);

        _provider.InitializeAssetsDirectory();
    }

    [Fact]
    public void GetFilePath_ValidFileName_ReturnsCorrectPath()
    {
        // Act
        string path = _provider.GetFilePath("ggml-base.bin");

        // Assert
        Assert.Contains("Assets", path);
        Assert.EndsWith("ggml-base.bin", path);
    }

    [Fact]
    public void GetFilePath_PathTraversalAttempt_ThrowsArgumentException()
    {
        // Arrange
        string maliciousFileName = "../../../Windows/System32/cmd.exe";

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => _provider.GetFilePath(maliciousFileName));
    }

    [Fact]
    public void CheckIfExists_ExistingFile_ReturnsTrue()
    {
        // Arrange
        // Using current directory which is guaranteed to exist
        string validPath = Directory.GetCurrentDirectory();

        // Act
        bool result = _provider.CheckIfExists(validPath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CheckIfExists_NonExistingFile_ReturnsFalse()
    {
        // Arrange
        string invalidPath = Path.Combine(Directory.GetCurrentDirectory(), "definitely_not_existing_file_12345.xyz");

        // Act
        bool result = _provider.CheckIfExists(invalidPath);

        // Assert
        Assert.False(result);
    }
}