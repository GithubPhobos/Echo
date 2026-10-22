using Echo.Setup.Audio;
using NAudio.Wave;

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

    [Fact]
    public void ReopenMicrophone_SystemClosedTheMicrophone_RecordsWhatTheNewOneHears()
    {
        // Arrange
        Devices("Headset");
        var closed = new Mock<IAudioInputStream>();
        var reopened = new Mock<IAudioInputStream>();
        Microphones(closed.Object, reopened.Object);
        using AudioRecordingService service = Service(index: 0, name: "Headset");
        service.InititalizeAudioRecordingCapabilities();

        // Act
        service.ReopenMicrophone();
        service.StartRecording();
        reopened.Raise(stream => stream.DataAvailable += null, Piece(bytes: 320));
        closed.Raise(stream => stream.DataAvailable += null, Piece(bytes: 640));

        // Assert
        closed.Verify(stream => stream.Dispose(), Times.Once);
        reopened.Verify(stream => stream.Start(), Times.Once);
        Assert.Equal(320, RecordedBytes(service));
    }

    [Fact]
    public void ReopenMicrophone_MicrophoneMovedMeanwhile_OpensItWhereItIsNow()
    {
        // Arrange
        Devices("Headset");
        using AudioRecordingService service = Service(index: 0, name: "Headset");
        service.InititalizeAudioRecordingCapabilities();
        Devices("Webcam", "Headset");

        // Act
        service.ReopenMicrophone();

        // Assert
        _audioInput.Verify(input => input.Open(1, It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void ReopenMicrophone_ClosedMicrophoneFailsToClose_OpensTheNewOneAnyway()
    {
        // Arrange
        Devices("Headset");
        var closed = new Mock<IAudioInputStream>();
        closed.Setup(stream => stream.Dispose()).Throws(new InvalidOperationException("The device is gone."));
        var reopened = new Mock<IAudioInputStream>();
        Microphones(closed.Object, reopened.Object);
        using AudioRecordingService service = Service(index: 0, name: "Headset");
        service.InititalizeAudioRecordingCapabilities();

        // Act
        Exception? exception = Record.Exception(service.ReopenMicrophone);

        // Assert
        Assert.Null(exception);
        reopened.Verify(stream => stream.Start(), Times.Once);
    }

    [Fact]
    public void StartRecording_MicrophoneDidNotOpenAgain_TriesOnceMore()
    {
        // Arrange: right after the return the headset is not back in the list yet
        Devices("Headset");
        var reopened = new Mock<IAudioInputStream>();
        Microphones(Mock.Of<IAudioInputStream>(), reopened.Object);
        using AudioRecordingService service = Service(index: 0, name: "Headset");
        service.InititalizeAudioRecordingCapabilities();
        Devices();
        service.ReopenMicrophone();
        Devices("Headset");

        // Act
        service.StartRecording();
        reopened.Raise(stream => stream.DataAvailable += null, Piece(bytes: 320));

        // Assert
        Assert.Equal(320, RecordedBytes(service));
    }

    [Fact]
    public void ReopenMicrophone_ServiceAlreadyClosed_OpensNothing()
    {
        // Arrange
        Devices("Headset");
        AudioRecordingService service = Service(index: 0, name: "Headset");
        service.InititalizeAudioRecordingCapabilities();
        service.Dispose();

        // Act
        service.ReopenMicrophone();

        // Assert
        _audioInput.Verify(input => input.Open(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    private void Devices(params string[] names) =>
        _audioInput.Setup(input => input.GetDevices()).Returns([.. names.Select((name, index) => new MicrophoneDevice(index, name))]);

    private AudioRecordingService Service(int index, string? name) =>
        new(_audioInput.Object,
            Mock.Of<ILogger<AudioRecordingService>>(),
            Options.Create(new RecordingDeviceSettings { DeviceIndex = index, DeviceName = name, BufferMs = 50, NumberOfBuffers = 3 }));

    /// <summary>
    ///     The microphones the system gives, one per opening, in this order.
    /// </summary>
    private void Microphones(params IAudioInputStream[] streams)
    {
        var openings = new Queue<IAudioInputStream>(streams);

        _audioInput
            .Setup(input => input.Open(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(openings.Dequeue);
    }

    /// <summary>
    ///     A piece of loud sound, as the microphone sends it.
    /// </summary>
    private static AudioInputEventArgs Piece(int bytes) =>
        new(Enumerable.Repeat((byte)0x40, bytes).ToArray(), bytes);

    /// <summary>
    ///     Ends the recording and says how much sound it holds, without the header of the file.
    /// </summary>
    private static long RecordedBytes(AudioRecordingService service)
    {
        using var reader = new WaveFileReader(service.StopRecording()!);

        return reader.Length;
    }
}