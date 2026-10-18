using Echo.Setup.Audio;

namespace Echo.Tests.UnitTests;

public sealed class AudioRecordingServiceTests
{
    private readonly Mock<IAudioInput> _audioInput = new();

    public AudioRecordingServiceTests()
    {
        _audioInput
            .Setup(input => input.Open(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(() => Mock.Of<IAudioInputStream>());
    }

    [Fact]
    public void InititalizeAudioRecordingCapabilities_MicrophoneMovedSinceSetup_OpensItWhereItIsNow()
    {
        // Arrange: setup saw the headset second, and since then a webcam has come before it
        Devices("Webcam", "Built-in microphone", "Headset");
        using AudioRecordingService service = Service(index: 1, name: "Headset");

        // Act
        service.InititalizeAudioRecordingCapabilities();

        // Assert
        _audioInput.Verify(input => input.Open(2, It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void InititalizeAudioRecordingCapabilities_TwoMicrophonesOfTheSameName_TakesTheOneAtTheSavedPlace()
    {
        // Arrange
        Devices("USB microphone", "USB microphone");
        using AudioRecordingService service = Service(index: 1, name: "USB microphone");

        // Act
        service.InititalizeAudioRecordingCapabilities();

        // Assert
        _audioInput.Verify(input => input.Open(1, It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void InititalizeAudioRecordingCapabilities_MicrophoneUnplugged_NamesItAndOpensNothing()
    {
        // Arrange: another microphone stands where the headset was
        Devices("Built-in microphone");
        using AudioRecordingService service = Service(index: 0, name: "Headset");

        // Act
        Exception exception = Assert.ThrowsAny<Exception>(service.InititalizeAudioRecordingCapabilities);

        // Assert
        Assert.Contains("Headset", exception.Message);
        Assert.Contains("/setup", exception.Message);
        _audioInput.Verify(input => input.Open(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void InititalizeAudioRecordingCapabilities_SettingsWithoutAName_TakeTheSavedPlace(string? name)
    {
        // Arrange
        Devices("Webcam", "Headset");
        using AudioRecordingService service = Service(index: 1, name);

        // Act
        service.InititalizeAudioRecordingCapabilities();

        // Assert
        _audioInput.Verify(input => input.Open(1, It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void InititalizeAudioRecordingCapabilities_SavedPlacePastTheEndOfTheList_SaysToChooseAgain()
    {
        // Arrange
        Devices("Webcam", "Headset");
        using AudioRecordingService service = Service(index: 2, name: null);

        // Act
        Exception exception = Assert.ThrowsAny<Exception>(service.InititalizeAudioRecordingCapabilities);

        // Assert
        Assert.Contains("/setup", exception.Message);
    }

    private void Devices(params string[] names) =>
        _audioInput.Setup(input => input.GetDevices()).Returns([.. names.Select((name, index) => new MicrophoneDevice(index, name))]);

    private AudioRecordingService Service(int index, string? name) =>
        new(_audioInput.Object,
            Mock.Of<ILogger<AudioRecordingService>>(),
            Options.Create(new RecordingDeviceSettings { DeviceIndex = index, DeviceName = name, BufferMs = 50, NumberOfBuffers = 3 }));
}