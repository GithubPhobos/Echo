using Echo.Listening;
using Echo.Services.BackgroundServices;

namespace Echo.Tests.UnitTests;

public sealed class EchoOrchestratorTests
{
    /// <summary>
    ///     How long a test waits for what it holds up. A fix that breaks the order fails the test instead of hanging it.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly Mock<IAssetsProvider> _assetsMock = new();
    private readonly Mock<ISoundPlayService> _soundMock = new();
    private readonly Mock<ITextInsertionService> _textMock = new();
    private readonly Mock<IAudioRecordingService> _audioMock = new();
    private readonly Mock<IWhisperInferenceService> _whisperMock = new();
    private readonly Mock<IPushToTalkMonitorService> _monitorMock = new();
    private readonly Mock<ILogger<EchoOrchestrator>> _loggerMock = new();

    private readonly EchoOrchestrator _orchestrator;

    public EchoOrchestratorTests()
    {
        _orchestrator = new EchoOrchestrator(
            _assetsMock.Object,
            _loggerMock.Object,
            _soundMock.Object,
            _textMock.Object,
            _audioMock.Object,
            _whisperMock.Object,
            _monitorMock.Object,
            Mock.Of<IErrorWindow>());
    }

    [Fact]
    public async Task ExecuteAsync_InitializesAllDependencies()
    {
        // Act
        await StartAsync(_orchestrator);

        // Assert
        _assetsMock.Verify(a => a.InitializeAssetsDirectory(), Times.Once);
        _audioMock.Verify(a => a.InititalizeAudioRecordingCapabilities(), Times.Once);
        _whisperMock.Verify(w => w.TryInitializeWhisperModel(), Times.Once);
        _monitorMock.Verify(m => m.InitializePushToTalkHotkeysOrThrow(), Times.Once);
        _soundMock.Verify(s => s.EnsureCanPlaySounds(), Times.Once);
        _monitorMock.Verify(m => m.StartListeningAsync(), Times.Once);
    }

    [Fact]
    public async Task OnRecordingTriggered_StartsAudioRecording()
    {
        // Arrange
        await StartAsync(_orchestrator);

        // Act
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);

        // Assert
        _audioMock.Verify(a => a.StartRecording(), Times.Once);
    }

    [Fact]
    public async Task ProcessRecordingPayloadAsync_ProcessesAudioAndInsertsText()
    {
        // Arrange
        var fakeStream = new MemoryStream(new byte[33000]);
        string expectedText = "Hello, world!";

        _audioMock.Setup(a => a.StopRecording())
                  .Returns(fakeStream);

        // We can safely match the exact stream instance now
        _whisperMock.Setup(w => w.ProcessAudioAsync(fakeStream))
                    .ReturnsAsync(expectedText);

        _textMock.Setup(t => t.InsertTextAsync(expectedText))
                 .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.ProcessRecordingPayloadAsync();

        // Assert
        _audioMock.Verify(a => a.StopRecording(), Times.Once);
        _whisperMock.Verify(w => w.ProcessAudioAsync(fakeStream), Times.Once);
        _textMock.Verify(t => t.InsertTextAsync(expectedText), Times.Once);
    }

    [Fact]
    public async Task OnRecordingReleased_WhisperThrowsException_LogsErrorAndDoesNotCrash()
    {
        // Arrange
        var fakeStream = new MemoryStream();
        _audioMock.Setup(a => a.StopRecording()).Returns(fakeStream);

        // Force Whisper service to throw an exception
        _whisperMock.Setup(w => w.ProcessAudioAsync(fakeStream))
            .ThrowsAsync(new InvalidOperationException("Whisper engine crashed!"));

        await StartAsync(_orchestrator);

        // Act
        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);

        // Stopping waits for the phrase that is still being recognized
        await _orchestrator.StopAsync(CancellationToken.None);

        // Assert
        // 1. App should not crash (we reach this line)
        // 2. Text insertion should NEVER be called since Whisper failed
        _textMock.Verify(t => t.InsertTextAsync(It.IsAny<string>()), Times.Never);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Whisper engine crashed!")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessRecordingPayloadAsync_PhraseRecognizedWhileEchoHasTheKeyboard_IsShownButNotTyped()
    {
        // Arrange
        var focused = true;
        var activity = new ListenActivity(new ScreenLog(), () => focused);

        var whisper = new Mock<IWhisperInferenceService>();
        whisper.Setup(service => service.ProcessAudioAsync(It.IsAny<MemoryStream?>())).ReturnsAsync("hello");

        var insertion = new Mock<ITextInsertionService>();

        var orchestrator = new EchoOrchestrator(
            Mock.Of<IAssetsProvider>(),
            Mock.Of<ILogger<EchoOrchestrator>>(),
            Mock.Of<ISoundPlayService>(),
            insertion.Object,
            Mock.Of<IAudioRecordingService>(),
            whisper.Object,
            Mock.Of<IPushToTalkMonitorService>(),
            Mock.Of<IErrorWindow>(),
            activity);

        // Act
        await orchestrator.ProcessRecordingPayloadAsync();

        focused = false;
        await orchestrator.ProcessRecordingPayloadAsync();

        // Assert: shown both times, typed only into another window
        Assert.Equal(2, activity.Log.Snapshot().Count(entry => entry.Kind == EntryKind.Dictated && entry.Text == "hello"));
        insertion.Verify(service => service.InsertTextAsync("hello"), Times.Once);
    }

    [Fact]
    public async Task OnRecordingReleased_NextPhraseStartedBeforeThePreviousIsRecognized_StaysRecording()
    {
        // Arrange
        var activity = new ListenActivity(new ScreenLog(), () => false);
        var firstPhrase = new TaskCompletionSource<string?>();

        _whisperMock.Setup(w => w.ProcessAudioAsync(It.IsAny<MemoryStream?>())).Returns(firstPhrase.Task);

        EchoOrchestrator orchestrator = CreateOrchestrator(activity);
        await StartAsync(orchestrator);

        // Act: the first phrase is said, and the next one begins while the first is recognized
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);
        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);

        firstPhrase.SetResult("first");
        await orchestrator.StopAsync(CancellationToken.None);

        // Assert: the end of the first phrase does not end the recording of the next one
        Assert.Equal(ListenState.Recording, activity.State);
    }

    [Fact]
    public async Task StopAsync_PhraseStillRecognized_WaitsForItBeforeTheModelIsUnloaded()
    {
        // Arrange
        var phrase = new TaskCompletionSource<string?>();

        _whisperMock.Setup(w => w.ProcessAudioAsync(It.IsAny<MemoryStream?>())).Returns(phrase.Task);

        await StartAsync(_orchestrator);
        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);

        // Act
        Task stopping = _orchestrator.StopAsync(CancellationToken.None);
        bool isStoppedTooEarly = stopping.IsCompleted;

        phrase.SetResult("hello");
        await stopping;

        // Assert
        Assert.False(isStoppedTooEarly);
        _textMock.Verify(t => t.InsertTextAsync("hello"), Times.Once);
    }

    [Fact]
    public async Task StopAsync_KeyReleasedJustAsEchoStops_StartsNoRecognitionAfterwards()
    {
        // Arrange: the handler of the released key is held up before it takes its phrase
        using var handlerEntered = new ManualResetEventSlim();
        using var handlerGoesOn = new ManualResetEventSlim();

        _loggerMock
            .Setup(x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("released")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(_ =>
            {
                handlerEntered.Set();
                handlerGoesOn.Wait(_patience);
            }));

        await StartAsync(_orchestrator);

        Task releasing = Task.Run(() => _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty));
        Assert.True(handlerEntered.Wait(_patience));

        // Act
        await _orchestrator.StopAsync(CancellationToken.None);

        handlerGoesOn.Set();
        await releasing.WaitAsync(_patience);

        // Assert: the handler found Echo stopping, so nothing is recognized once the model may be gone
        _whisperMock.Verify(w => w.ProcessAudioAsync(It.IsAny<MemoryStream?>()), Times.Never);
    }

    [Fact]
    public async Task StopAsync_StartUpStillUnderWay_WaitsForItAndNeverListensToTheKey()
    {
        // Arrange: the microphone takes its time to answer
        using var deviceAsked = new ManualResetEventSlim();
        using var deviceAnswers = new ManualResetEventSlim();

        _audioMock.Setup(a => a.InititalizeAudioRecordingCapabilities()).Callback(() =>
        {
            deviceAsked.Set();
            deviceAnswers.Wait(_patience);
        });

        await _orchestrator.StartAsync(CancellationToken.None);
        Assert.True(deviceAsked.Wait(_patience));

        // Act: the host has run out of patience, so stopping is asked to hurry
        Task stopping = _orchestrator.StopAsync(new CancellationToken(canceled: true));
        bool isStoppedTooEarly = stopping.IsCompleted;

        deviceAnswers.Set();
        await stopping.WaitAsync(_patience);

        // Assert: stopping waited for the start-up, and the start-up never took the key
        Assert.False(isStoppedTooEarly);
        _monitorMock.VerifyAdd(m => m.OnRecordingTriggered += It.IsAny<EventHandler>(), Times.Never);
        _monitorMock.Verify(m => m.StartListeningAsync(), Times.Never);
    }

    [Fact]
    public async Task OnRecordingReleased_TwoPhrasesOneAfterTheOther_EachIsPastedWhole()
    {
        // Arrange: the real insertion, with the clipboard and the keyboard stood in for
        string? clipboard = null;
        var pasted = new List<string?>();

        var clipboardMock = new Mock<ITextClipboard>();
        clipboardMock
            .Setup(c => c.SetTextAsync(It.IsAny<string>()))
            .Callback((string text) => clipboard = text)
            .Returns(Task.CompletedTask);

        var pasteMock = new Mock<IPasteKeys>();
        pasteMock
            .Setup(k => k.PasteAsync())
            .Callback(() => pasted.Add(clipboard))
            .ReturnsAsync(true);

        var insertion = new TextInsertionService(
            Options.Create(new TextInsertionSettings { UseAutoInsert = true }),
            clipboardMock.Object,
            pasteMock.Object,
            Mock.Of<ILogger<TextInsertionService>>());

        _whisperMock.SetupSequence(w => w.ProcessAudioAsync(It.IsAny<MemoryStream?>()))
            .ReturnsAsync("first")
            .ReturnsAsync("second");

        var orchestrator = new EchoOrchestrator(
            _assetsMock.Object,
            _loggerMock.Object,
            _soundMock.Object,
            insertion,
            _audioMock.Object,
            _whisperMock.Object,
            _monitorMock.Object,
            Mock.Of<IErrorWindow>());

        await StartAsync(orchestrator);

        // Act
        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);
        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);

        await orchestrator.StopAsync(CancellationToken.None);

        // Assert: the second phrase never took the clipboard before the first one was pasted
        Assert.Equal(["first", "second"], pasted);
    }

    /// <summary>
    ///     Starts the orchestrator and waits for its start-up, which runs in the background
    ///     and subscribes to the key at its end.
    /// </summary>
    private static async Task StartAsync(EchoOrchestrator orchestrator)
    {
        await orchestrator.StartAsync(CancellationToken.None);
        await orchestrator.ExecuteTask!;
    }

    private EchoOrchestrator CreateOrchestrator(IListenActivity activity) =>
        new(
            _assetsMock.Object,
            _loggerMock.Object,
            _soundMock.Object,
            _textMock.Object,
            _audioMock.Object,
            _whisperMock.Object,
            _monitorMock.Object,
            Mock.Of<IErrorWindow>(),
            activity);
}