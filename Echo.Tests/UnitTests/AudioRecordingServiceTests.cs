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
        Reopen(service);
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
        Reopen(service);

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
        Exception? exception = Record.Exception(() => Reopen(service));

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
        Reopen(service);
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
        Reopen(service);

        // Assert
        _audioInput.Verify(input => input.Open(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public void StartRecording_MicrophoneUnplugged_RecordsNothingAndSaysWhatToDo()
    {
        // Arrange: the headset went away while Echo ran, another microphone stands where it was,
        // and the going away was noticed
        Devices("Headset");
        var activity = new ListenActivity(new ScreenLog(), () => false);
        using AudioRecordingService service = Service(index: 0, name: "Headset", activity);
        service.InititalizeAudioRecordingCapabilities();
        Devices("Built-in microphone");
        Reopen(service);

        // Act
        bool isRecording = service.StartRecording();

        // Assert: the other microphone is not taken in its place, and the screen says what to do each time
        Assert.False(isRecording);
        Assert.Null(service.StopRecording());
        _audioInput.Verify(input => input.Open(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Once);

        string notConnected = Strings.Format(nameof(Strings.MicrophoneNotConnected), "Headset");
        Assert.Equal([notConnected, notConnected], Said(activity, EntryKind.Warning));
    }

    [Fact]
    public void StartRecording_MicrophoneThereButDoesNotOpen_SaysToPlugItInAgain()
    {
        // Arrange: after the first opening the headset stays in the list but fails to open
        Devices("Headset");
        int openings = 0;

        _audioInput
            .Setup(input => input.Open(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(() => ++openings == 1 ? Mock.Of<IAudioInputStream>() : throw new InvalidOperationException("The device is busy."));

        var activity = new ListenActivity(new ScreenLog(), () => false);
        using AudioRecordingService service = Service(index: 0, name: "Headset", activity);
        service.InititalizeAudioRecordingCapabilities();
        Reopen(service);

        // Act
        bool isRecording = service.StartRecording();

        // Assert
        Assert.False(isRecording);

        string notOpening = Strings.Format(nameof(Strings.MicrophoneNotOpening), "Headset");
        Assert.Equal([notOpening, notOpening], Said(activity, EntryKind.Warning));
    }

    [Fact]
    public void ReopenMicrophone_MicrophonePluggedInAgain_SaysOnceThatItIsBack()
    {
        // Arrange
        Devices("Headset");
        var activity = new ListenActivity(new ScreenLog(), () => false);
        using AudioRecordingService service = Service(index: 0, name: "Headset", activity);
        service.InititalizeAudioRecordingCapabilities();
        Devices();
        Reopen(service);
        Devices("Headset");

        // Act
        Reopen(service);
        Reopen(service);

        // Assert
        Assert.Equal([Strings.Format(nameof(Strings.MicrophoneBack), "Headset")], Said(activity, EntryKind.Message));
        Assert.True(service.StartRecording());
    }

    [Fact]
    public void ReopenMicrophone_MicrophoneNeverWentAway_SaysNothing()
    {
        // Arrange: after sleep the headset was back before Echo looked for it
        Devices("Headset");
        var activity = new ListenActivity(new ScreenLog(), () => false);
        using AudioRecordingService service = Service(index: 0, name: "Headset", activity);
        service.InititalizeAudioRecordingCapabilities();

        // Act
        Reopen(service);

        // Assert
        Assert.Empty(activity.Log.Snapshot());
    }

    [Fact]
    public void StartRecording_KeyPressedBeforeTheMarkedMicrophoneIsOpenedAgain_OpensItAgainFirst()
    {
        // Arrange: Echo has heard that the system may have closed the microphone, and the key comes before the opening
        Devices("Headset");
        var closed = new Mock<IAudioInputStream>();
        var reopened = new Mock<IAudioInputStream>();
        Microphones(closed.Object, reopened.Object);
        using AudioRecordingService service = Service(index: 0, name: "Headset");
        service.InititalizeAudioRecordingCapabilities();
        service.MarkMicrophoneForReopening();

        // Act: the phrase is said, and the opening comes in the middle of it
        bool isRecording = service.StartRecording();
        reopened.Raise(stream => stream.DataAvailable += null, Piece(bytes: 320));
        bool isLeftToTheOpening = service.ReopenMicrophone();
        reopened.Raise(stream => stream.DataAvailable += null, Piece(bytes: 320));

        // Assert: the phrase was recorded whole from the microphone opened again, and nothing was left to open
        Assert.True(isRecording);
        Assert.True(isLeftToTheOpening);
        Assert.Equal(640, RecordedBytes(service));
        closed.Verify(stream => stream.Dispose(), Times.Once);
        reopened.Verify(stream => stream.Dispose(), Times.Never);
        _audioInput.Verify(input => input.Open(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(2));
    }

    [Fact]
    public void ReopenMicrophone_RecordingBeganBeforeTheMark_LeavesItWholeAndOpensTheMicrophoneAfterIt()
    {
        // Arrange: the phrase had begun when Echo heard that the microphone may have been closed
        Devices("Headset");
        var closed = new Mock<IAudioInputStream>();
        var reopened = new Mock<IAudioInputStream>();
        Microphones(closed.Object, reopened.Object);
        using AudioRecordingService service = Service(index: 0, name: "Headset");
        service.InititalizeAudioRecordingCapabilities();
        service.StartRecording();
        service.MarkMicrophoneForReopening();

        // Act
        bool isOpenedDuringThePhrase = service.ReopenMicrophone();
        closed.Raise(stream => stream.DataAvailable += null, Piece(bytes: 320));
        long recorded = RecordedBytes(service);
        bool isOpenedAfterIt = service.ReopenMicrophone();

        // Assert
        Assert.False(isOpenedDuringThePhrase);
        Assert.Equal(320, recorded);
        Assert.True(isOpenedAfterIt);
        closed.Verify(stream => stream.Dispose(), Times.Once);
        reopened.Verify(stream => stream.Start(), Times.Once);
    }

    [Fact]
    public void StartRecording_MicrophoneLeftMarkedByAPhrase_OpensItAgainBeforeTheNextOne()
    {
        // Arrange: the opening the phrase held off has not come yet when the next phrase begins
        Devices("Headset");
        var closed = new Mock<IAudioInputStream>();
        var reopened = new Mock<IAudioInputStream>();
        Microphones(closed.Object, reopened.Object);
        using AudioRecordingService service = Service(index: 0, name: "Headset");
        service.InititalizeAudioRecordingCapabilities();
        service.StartRecording();
        service.MarkMicrophoneForReopening();
        service.ReopenMicrophone();
        service.StopRecording();

        // Act
        service.StartRecording();
        reopened.Raise(stream => stream.DataAvailable += null, Piece(bytes: 320));

        // Assert
        Assert.Equal(320, RecordedBytes(service));
        closed.Verify(stream => stream.Dispose(), Times.Once);
    }

    private void Devices(params string[] names) =>
        _audioInput.Setup(input => input.GetDevices()).Returns([.. names.Select((name, index) => new MicrophoneDevice(index, name))]);

    private AudioRecordingService Service(int index, string? name, IListenActivity? activity = null) =>
        new(_audioInput.Object,
            Mock.Of<ILogger<AudioRecordingService>>(),
            Options.Create(new RecordingDeviceSettings { DeviceIndex = index, DeviceName = name, BufferMs = 50, NumberOfBuffers = 3 }),
            activity);

    /// <summary>
    ///     What Echo does when the system may have closed the microphone: marks it at once, then opens it again.
    /// </summary>
    private static bool Reopen(AudioRecordingService service)
    {
        service.MarkMicrophoneForReopening();

        return service.ReopenMicrophone();
    }

    /// <summary>
    ///     The lines of one kind the service put on the screen, in order.
    /// </summary>
    private static string[] Said(ListenActivity activity, EntryKind kind) =>
        [.. activity.Log.Snapshot().Where(entry => entry.Kind == kind).Select(entry => entry.Text)];

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