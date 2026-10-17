using Echo.Setup;
using Microsoft.Extensions.Configuration;

namespace Echo.Tests.UnitTests;

public sealed class UserSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public UserSettingsStoreTests()
    {
        Directory.CreateDirectory(_directory);

        _paths = new AppPaths(_directory, _directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Load_NoSettingsFile_ReportsSetupAsNotCompleted()
    {
        // Act
        UserSettings settings = new UserSettingsStore(_paths).Load();

        // Assert
        Assert.Equal(0, settings.SetupVersion);
        Assert.Null(settings.ModelName);
    }

    [Fact]
    public void Load_UnreadableSettingsFile_IsTreatedAsNotSetUp()
    {
        // Arrange
        File.WriteAllText(_paths.SettingsFile, "{ this is not json");

        // Act & Assert
        Assert.Equal(0, new UserSettingsStore(_paths).Load().SetupVersion);
    }

    [Fact]
    public void Save_ThenLoad_ReturnsTheSameChoices()
    {
        // Arrange
        var store = new UserSettingsStore(_paths);
        var settings = new UserSettings
        {
            SetupVersion = 1,
            UiLanguage = "ru",
            DictationLanguage = "ru",
            Additional = new AdditionalValues()
                .With(AdditionalSettings.Translate, AdditionalSettings.On)
                .With(AdditionalSettings.Prompt, "Kubernetes, gRPC")
                .With(AdditionalSettings.Threads, "8")
                .With(AdditionalSettings.Volume, "0.35")
                .With(AdditionalSettings.PlaySound, AdditionalSettings.Off),
            HardwareBackend = "Cuda",
            GraphicsCardIndex = 1,
            ModelName = "ggml-large-v3.bin",
            HotkeyKey = "VcF8",
            HotkeyModifier = "leftctrl",
            MicrophoneIndex = 2,
            MicrophoneName = "Микрофон (USB Audio)",
            SilenceThreshold = 0.012f,
        };

        // Act
        store.Save(settings);

        // Assert
        Assert.Equal(settings, store.Load());
        Assert.Contains("Микрофон", File.ReadAllText(_paths.SettingsFile));
    }

    [Fact]
    public void Save_FileWithUsersOwnSettings_KeepsThem()
    {
        // Arrange: a prompt added by hand, with a comment, the way the bundled defaults are written
        File.WriteAllText(_paths.SettingsFile, """
            {
              // my dictionary
              "WhisperSettings": { "Prompt": "Kubernetes, gRPC", "ModelName": "ggml-base.bin" }
            }
            """);

        var store = new UserSettingsStore(_paths);

        // Act
        store.Save(store.Load() with { ModelName = "ggml-small.bin", SetupVersion = 1 });

        // Assert
        IConfiguration config = new ConfigurationBuilder().AddJsonFile(_paths.SettingsFile).Build();

        Assert.Equal("Kubernetes, gRPC", config["WhisperSettings:Prompt"]);
        Assert.Equal("ggml-small.bin", config["WhisperSettings:ModelName"]);
        Assert.Equal("1", config["Setup:Version"]);
    }

    [Fact]
    public void Save_SavedSettings_AreReadByTheDictationServices()
    {
        // Arrange
        new UserSettingsStore(_paths).Save(new UserSettings
        {
            SetupVersion = 1,
            DictationLanguage = "de",
            HardwareBackend = "Vulkan",
            ModelName = "ggml-medium.bin",
            HotkeyKey = "VcF8",
            HotkeyModifier = "leftalt",
            MicrophoneIndex = 3,
            SilenceThreshold = 0.02f,
        });

        // Act
        IConfiguration config = Echo.Extensions.ServiceCollectionExtensions.BuildConfiguration(_paths);

        // Assert
        var whisper = config.GetSection(nameof(WhisperSettings)).Get<WhisperSettings>()!;

        Assert.Equal("de", whisper.Language);
        Assert.Equal("Vulkan", whisper.HardwareBackend);
        Assert.Equal("ggml-medium.bin", whisper.ModelName);
        Assert.Equal("VcF8", config["PushToTalkSettings:Key"]);
        Assert.Equal("leftalt", config["PushToTalkSettings:Modifier"]);
        Assert.Equal("3", config["RecordingDeviceSettings:DeviceIndex"]);

        // Everything the wizard does not ask about still comes from the bundled defaults
        Assert.True(whisper.SuppressBlank);
    }

    [Fact]
    public void Save_LogLevel_IsWrittenWhereTheLoggerReadsIt()
    {
        // Arrange
        var store = new UserSettingsStore(_paths);

        // Act: the level lives in a section within a section
        store.Save(new UserSettings { Additional = new AdditionalValues().With(AdditionalSettings.LogLevel, "Warning") });

        string? chosen = Echo.Extensions.ServiceCollectionExtensions.BuildConfiguration(_paths)["Serilog:MinimumLevel:Default"];
        string? loaded = store.Load().Additional.Get(AdditionalSettings.LogLevel);

        store.Save(new UserSettings());

        string? shipped = Echo.Extensions.ServiceCollectionExtensions.BuildConfiguration(_paths)["Serilog:MinimumLevel:Default"];

        // Assert
        Assert.Equal("Warning", chosen);
        Assert.Equal("Warning", loaded);

        // Left alone, it is not kept, so the level the program ships with applies
        Assert.Equal(AdditionalSettings.LogLevel.Default, shipped);
        Assert.Null(store.Load().Additional.Stored(AdditionalSettings.LogLevel));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ broken json")]
    [InlineData("[1, 2]")]
    public void BuildConfiguration_DamagedSettingsFile_UsesTheShippedSettings(string content)
    {
        // Arrange
        File.WriteAllText(_paths.SettingsFile, content);

        // Act
        IConfiguration config = Echo.Extensions.ServiceCollectionExtensions.BuildConfiguration(_paths);

        // Assert: nothing fails before the setup wizard writes a fresh file
        Assert.Equal(AdditionalSettings.LogLevel.Default, config["Serilog:MinimumLevel:Default"]);
    }

    [Fact]
    public void Save_DamagedSettingsFile_KeepsItAsideAndWritesAFreshOne()
    {
        // Arrange
        File.WriteAllText(_paths.SettingsFile, "{ broken json");

        var store = new UserSettingsStore(_paths);

        // Act
        store.Save(new UserSettings { SetupVersion = 1 });

        // Assert
        Assert.Equal("{ broken json", File.ReadAllText(_paths.SettingsFile + ".damaged"));
        Assert.Equal(1, store.Load().SetupVersion);
    }

    [Fact]
    public void Load_ValuesTheSettingsCannotTake_AreLeftOutAndRemovedOnSave()
    {
        // Arrange: values typed by hand that no setting accepts
        File.WriteAllText(_paths.SettingsFile, """
            {
              "WhisperSettings": { "Threads": "bad", "Temperature": 5, "Prompt": "Kubernetes" },
              "RecordingDeviceSettings": { "BufferMs": 50.5 },
              "Serilog": { "MinimumLevel": { "Default": "Loud" } }
            }
            """);

        var store = new UserSettingsStore(_paths);

        // Act
        UserSettings loaded = store.Load();
        store.Save(loaded);

        // Assert: the shipped values apply instead, and what is right is kept
        Assert.Null(loaded.Additional.Stored(AdditionalSettings.Threads));
        Assert.Null(loaded.Additional.Stored(AdditionalSettings.Temperature));
        Assert.Null(loaded.Additional.Stored(AdditionalSettings.BufferMs));
        Assert.Null(loaded.Additional.Stored(AdditionalSettings.LogLevel));
        Assert.Equal("Kubernetes", loaded.Additional.Stored(AdditionalSettings.Prompt));

        IConfiguration config = new ConfigurationBuilder().AddJsonFile(_paths.SettingsFile).Build();

        Assert.Null(config["WhisperSettings:Threads"]);
        Assert.Equal("Kubernetes", config["WhisperSettings:Prompt"]);
    }

    [Fact]
    public void BuildConfiguration_ValuesTheSettingsCannotTake_RunWithTheShippedOnesWithoutSavingFirst()
    {
        // Arrange: set up, then edited by hand; the wizard does not open by itself for this
        File.WriteAllText(_paths.SettingsFile, """
            {
              "Setup": { "Version": 1 },
              "WhisperSettings": { "Threads": "bad", "Prompt": "Kubernetes" },
              "RecordingDeviceSettings": { "BufferMs": 50.5 },
              "Serilog": { "MinimumLevel": { "Default": "Loud" } }
            }
            """);

        // Act
        IConfiguration config = Echo.Extensions.ServiceCollectionExtensions.BuildConfiguration(_paths);

        WhisperSettings whisper = config.GetSection(nameof(WhisperSettings)).Get<WhisperSettings>()!;
        RecordingDeviceSettings recording = config.GetSection(nameof(RecordingDeviceSettings)).Get<RecordingDeviceSettings>()!;

        // Assert: the services can be made, with what the wizard would read, and what is right is kept
        Assert.Equal(1, new UserSettingsStore(_paths).Load().SetupVersion);
        Assert.Null(whisper.Threads);
        Assert.Equal("Kubernetes", whisper.Prompt);
        Assert.Equal(50, recording.BufferMs);
        Assert.Equal(AdditionalSettings.LogLevel.Default, config["Serilog:MinimumLevel:Default"]);
    }
}