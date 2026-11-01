namespace Echo.Tests.UnitTests;

/// <summary>
///     Checks the history's configuration through the same registration paths as dictation and setup.
/// </summary>
public sealed class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RegisterServices_SavedHistoryChoice_IsUsedByTheWriter(bool enabled)
    {
        // Arrange: exercise the real save, load, configuration binding and writer, without audio hardware
        var paths = new AppPaths(_directory, _directory);
        new UserSettingsStore(paths).Save(new UserSettings
        {
            Additional = new AdditionalValues().With(AdditionalSettings.Memory, enabled ? AdditionalSettings.On : AdditionalSettings.Off),
        });
        var services = new ServiceCollection();
        services.AddSingleton<IAppPaths>(paths);
        services.SetupConfiguration(ServiceCollectionExtensions.BuildConfiguration(paths));
        services.RegisterServices();
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        provider.GetRequiredService<IDictationMemory>().Add("saved setting", TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        // Assert
        Assert.Equal(enabled, provider.GetRequiredService<IOptions<MemorySettings>>().Value.Enabled);
        Assert.Equal(enabled, Directory.Exists(paths.MemoryDirectory));
    }

    [Fact]
    public void RegisterServices_NoPreviousHistoryChoice_KeepsHistoryByDefault()
    {
        // Arrange: the same situation as an existing installation upgraded to this version
        var paths = new AppPaths(_directory, _directory);
        var services = new ServiceCollection();
        services.AddSingleton<IAppPaths>(paths);
        services.SetupConfiguration(ServiceCollectionExtensions.BuildConfiguration(paths));
        services.RegisterServices();
        using ServiceProvider provider = services.BuildServiceProvider();

        // Act
        provider.GetRequiredService<IDictationMemory>().Add("first phrase", TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        // Assert
        Assert.True(provider.GetRequiredService<IOptions<MemorySettings>>().Value.Enabled);
        Assert.Single(Directory.GetFiles(paths.MemoryDirectory, "*.jsonl"));
        using JsonDocument entry = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(paths.MemoryDirectory, "*.jsonl")[0]));
        Assert.False(entry.RootElement.TryGetProperty("ws", out _));
    }

    [Fact]
    public void RegisterDictationServices_SetupTest_DoesNotRegisterOrCreateHistory()
    {
        // Arrange: DictationTester uses this registration instead of RegisterServices
        var paths = new AppPaths(_directory, _directory);
        var services = new ServiceCollection();
        services.AddSingleton<IAppPaths>(paths);
        services.SetupConfiguration(ServiceCollectionExtensions.BuildConfiguration(paths));

        // Act
        services.RegisterDictationServices();
        using ServiceProvider provider = services.BuildServiceProvider();

        // Assert
        Assert.Null(provider.GetService<IDictationMemory>());
        Assert.False(Directory.Exists(paths.MemoryDirectory));
    }
}