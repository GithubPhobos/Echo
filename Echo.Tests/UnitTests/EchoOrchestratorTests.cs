using System.Collections.Concurrent;
using Echo.Listening;
using Echo.Services.BackgroundServices;

namespace Echo.Tests.UnitTests;

public sealed class EchoOrchestratorTests
{
    /// <summary>
    ///     How long a test waits for what it holds up. A fix that breaks the order fails the test instead of hanging it.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly Mock<ISoundPlayService> _soundMock = new();
    private readonly Mock<ITextInsertionService> _textMock = new();
    private readonly Mock<IAudioRecordingService> _audioMock = new();
    private readonly Mock<IWhisperInferenceService> _whisperMock = new();
    private readonly Mock<IPushToTalkMonitorService> _monitorMock = new();
    private readonly Mock<ILogger<EchoOrchestrator>> _loggerMock = new();
    private readonly Mock<ISessionEvents> _sessionMock = new();
    private readonly Mock<IAudioDeviceEvents> _devicesMock = new();

    private readonly EchoOrchestrator _orchestrator;

    public EchoOrchestratorTests()
    {
        // The microphone is there unless a test unplugs it
        _audioMock.Setup(a => a.StartRecording()).Returns(true);

        _orchestrator = new EchoOrchestrator(
            _loggerMock.Object,
            _soundMock.Object,
            _textMock.Object,
            _audioMock.Object,
            _whisperMock.Object,
            _monitorMock.Object,
            Mock.Of<IErrorWindow>(),
            sessionEvents: _sessionMock.Object,
            deviceEvents: _devicesMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_InitializesAllDependencies()
    {
        // Act
        await StartAsync(_orchestrator);

        // Assert
        _audioMock.Verify(a => a.InititalizeAudioRecordingCapabilities(), Times.Once);
        _whisperMock.Verify(w => w.TryInitializeWhisperModel(), Times.Once);
        _monitorMock.Verify(m => m.InitializePushToTalkHotkeysOrThrow(), Times.Once);
        _soundMock.Verify(s => s.EnsureCanPlaySounds(), Times.Once);
        _monitorMock.Verify(m => m.StartListeningAsync(It.IsAny<CancellationToken>()), Times.Once);
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

        // We can safely match the exact stream instance now
        _whisperMock.Setup(w => w.ProcessAudioAsync(fakeStream))
                    .ReturnsAsync(expectedText);

        _textMock.Setup(t => t.InsertTextAsync(expectedText))
                 .Returns(Task.CompletedTask);

        // Act
        await _orchestrator.ProcessRecordingPayloadAsync(fakeStream);

        // Assert
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
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnRecordingReleased_SaysItRecognizesOnlyARecordingWithSound(bool hasSound)
    {
        // Arrange: a recording long enough to be heard, loud after the 44 bytes of its header or silent
        byte[] audio = new byte[20000];

        if (hasSound)
        {
            audio[50] = 0x88;
            audio[51] = 0x13;
        }

        _audioMock.Setup(a => a.StopRecording()).Returns(new MemoryStream(audio));

        await StartAsync(_orchestrator);

        // Act
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);
        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);
        await _orchestrator.StopAsync(CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Recorded") && v.ToString()!.Contains("recognizing") == hasSound),
                It.IsAny<Exception?>(),
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
            Mock.Of<ILogger<EchoOrchestrator>>(),
            Mock.Of<ISoundPlayService>(),
            insertion.Object,
            Mock.Of<IAudioRecordingService>(),
            whisper.Object,
            Mock.Of<IPushToTalkMonitorService>(),
            Mock.Of<IErrorWindow>(),
            activity);

        // Act
        await orchestrator.ProcessRecordingPayloadAsync(new MemoryStream());

        focused = false;
        await orchestrator.ProcessRecordingPayloadAsync(new MemoryStream());

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
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);
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
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Recorded")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(_ =>
            {
                handlerEntered.Set();
                handlerGoesOn.Wait(_patience);
            }));

        await StartAsync(_orchestrator);
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);

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
        _monitorMock.Verify(m => m.StartListeningAsync(It.IsAny<CancellationToken>()), Times.Never);
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
            _loggerMock.Object,
            _soundMock.Object,
            insertion,
            _audioMock.Object,
            _whisperMock.Object,
            _monitorMock.Object,
            Mock.Of<IErrorWindow>());

        await StartAsync(orchestrator);

        // Act
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);
        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);
        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);

        await orchestrator.StopAsync(CancellationToken.None);

        // Assert: the second phrase never took the clipboard before the first one was pasted
        Assert.Equal(["first", "second"], pasted);
    }

    [Fact]
    public async Task StopAsync_DesktopStillAsksTheUserForTheKey_StopsWithoutWaitingForTheAnswer()
    {
        // Arrange: the desktop's window that gives the key stays open
        var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _monitorMock
            .Setup(m => m.StartListeningAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(token =>
            {
                asking.TrySetResult();

                return Task.Delay(Timeout.Infinite, token);
            });

        // The start-up runs in the background: stopped before it began, it would have nothing to wait for
        await _orchestrator.StartAsync(CancellationToken.None);
        await asking.Task.WaitAsync(_patience);

        // Act
        await _orchestrator.StopAsync(CancellationToken.None).WaitAsync(_patience);

        // Assert: the waiting was given up, and nothing counts as failed
        Assert.True(_orchestrator.ExecuteTask!.IsCompletedSuccessfully);
        _textMock.Verify(t => t.PrepareAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_GetsReadyToPasteBeforeItIsReady()
    {
        // Act
        await StartAsync(_orchestrator);

        // Assert: a permission to renew is renewed when Echo starts, not in the middle of dictation
        _textMock.Verify(t => t.PrepareAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnRecordingCancelled_KeyLostWhileRecording_DropsThePhraseAndListensAgain()
    {
        // Arrange
        var activity = new ListenActivity(new ScreenLog(), () => false);
        EchoOrchestrator orchestrator = CreateOrchestrator(activity);
        await StartAsync(orchestrator);

        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);

        // Act: the desktop took the key back while it was held
        _monitorMock.Raise(m => m.OnRecordingCancelled += null, EventArgs.Empty);
        await orchestrator.StopAsync(CancellationToken.None);

        // Assert: the recording ended, and nothing was recognized or typed
        _audioMock.Verify(a => a.StopRecording(), Times.Once);
        _whisperMock.Verify(w => w.ProcessAudioAsync(It.IsAny<MemoryStream?>()), Times.Never);
        _textMock.Verify(t => t.InsertTextAsync(It.IsAny<string?>()), Times.Never);
        Assert.Equal(ListenState.Listening, activity.State);
    }

    [Fact]
    public async Task OnRecordingTriggered_KeyPressedWhileTheModelLoads_RecordsAndRecognizesOnceItIsLoaded()
    {
        // Arrange: the model takes its time to load
        using var loading = new ManualResetEventSlim();
        using var loaded = new ManualResetEventSlim();

        _whisperMock.Setup(w => w.TryInitializeWhisperModel()).Callback(() =>
        {
            loading.Set();
            loaded.Wait(_patience);
        });

        _whisperMock.Setup(w => w.ProcessAudioAsync(It.IsAny<MemoryStream?>())).ReturnsAsync("hello");

        var activity = new ListenActivity(new ScreenLog(), () => false);
        EchoOrchestrator orchestrator = CreateOrchestrator(activity);

        await orchestrator.StartAsync(CancellationToken.None);
        Assert.True(loading.Wait(_patience));

        // Act: the key is already Echo's, so a phrase can be said while the model loads
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);
        bool isRecording = activity.State == ListenState.Recording;

        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);
        bool isRecognizedTooEarly = _whisperMock.Invocations.Any(call => call.Method.Name == nameof(IWhisperInferenceService.ProcessAudioAsync));

        loaded.Set();
        await orchestrator.ExecuteTask!.WaitAsync(_patience);
        await orchestrator.StopAsync(CancellationToken.None);

        // Assert: recorded at once, recognized and typed only once the model was there
        Assert.True(isRecording);
        Assert.False(isRecognizedTooEarly);
        _audioMock.Verify(a => a.StartRecording(), Times.Once);
        _textMock.Verify(t => t.InsertTextAsync("hello"), Times.Once);
        Assert.Equal(ListenState.Listening, activity.State);
    }

    [Fact]
    public async Task ExecuteAsync_ModelFailsAfterTheKeyWasTaken_GivesTheKeyBackAndDropsThePhrase()
    {
        // Arrange: a phrase is said while the model loads, and the model cannot be loaded
        using var loading = new ManualResetEventSlim();
        using var failing = new ManualResetEventSlim();

        _whisperMock.Setup(w => w.TryInitializeWhisperModel()).Callback(() =>
        {
            loading.Set();
            failing.Wait(_patience);

            throw new FileNotFoundException("Model 'ggml-large-v3.bin' was not found.");
        });

        var activity = new ListenActivity(new ScreenLog(), () => false);
        EchoOrchestrator orchestrator = CreateOrchestrator(activity);

        await orchestrator.StartAsync(CancellationToken.None);
        Assert.True(loading.Wait(_patience));

        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);
        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);

        // Act
        failing.Set();
        await orchestrator.ExecuteTask!.WaitAsync(_patience);
        await orchestrator.StopAsync(CancellationToken.None);

        // Assert: the key does what it does without Echo again, nothing is recognized, and the screen says Echo failed
        _monitorMock.Verify(m => m.ReleaseAsync(), Times.Once);
        _whisperMock.Verify(w => w.ProcessAudioAsync(It.IsAny<MemoryStream?>()), Times.Never);
        _textMock.Verify(t => t.InsertTextAsync(It.IsAny<string?>()), Times.Never);
        Assert.Equal(ListenState.Failed, activity.State);
    }

    [Fact]
    public async Task ExecuteAsync_ModelFailsWhileTheKeyIsHeld_GivesTheKeyBackToTheSystemAndEndsTheRecording()
    {
        // Arrange: the real monitor between the key and Echo; the key is held while the model fails to load
        var listener = new Mock<IHotkeyListener>();
        using PushToTalkMonitorService monitor = CreateMonitor(listener);

        using var loading = new ManualResetEventSlim();
        using var failing = new ManualResetEventSlim();
        using var recording = new SemaphoreSlim(0);
        using var stopped = new SemaphoreSlim(0);

        _whisperMock.Setup(w => w.TryInitializeWhisperModel()).Callback(() =>
        {
            loading.Set();
            failing.Wait(_patience);

            throw new IOException("The model file is damaged.");
        });

        _audioMock.Setup(a => a.StartRecording()).Callback(() => recording.Release()).Returns(true);
        _audioMock.Setup(a => a.StopRecording()).Callback(() => stopped.Release()).Returns((MemoryStream?)null);

        var activity = new ListenActivity(new ScreenLog(), () => false);
        EchoOrchestrator orchestrator = CreateOrchestrator(activity, monitor);

        await orchestrator.StartAsync(CancellationToken.None);
        Assert.True(loading.Wait(_patience));

        listener.Raise(l => l.Pressed += null, new HotkeyEventArgs());
        Assert.True(await recording.WaitAsync(_patience));

        // Act
        failing.Set();
        await orchestrator.ExecuteTask!.WaitAsync(_patience);

        // Assert: the system holds the key for Echo no more, the recording its coming up would have ended is ended,
        // and the screen says Echo failed
        listener.Verify(l => l.ReleaseAsync(), Times.Once);
        Assert.True(await stopped.WaitAsync(_patience));

        await orchestrator.StopAsync(CancellationToken.None);

        Assert.Equal(ListenState.Failed, activity.State);
    }

    [Fact]
    public async Task OnRecordingCancelled_KeyLostBeforeThePressIsHandled_EndsTheRecordingThePressStarts()
    {
        // Arrange: the real monitor between the key and Echo; Echo is held up with the press before it records
        var listener = new Mock<IHotkeyListener>();
        using PushToTalkMonitorService monitor = CreateMonitor(listener);

        using var handling = new ManualResetEventSlim();
        using var handled = new ManualResetEventSlim();
        using var stopped = new SemaphoreSlim(0);

        _loggerMock
            .Setup(logger => logger.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("Recording...")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(_ =>
            {
                handling.Set();
                handled.Wait(_patience);
            }));

        _audioMock.Setup(a => a.StopRecording()).Callback(() => stopped.Release()).Returns((MemoryStream?)null);

        var activity = new ListenActivity(new ScreenLog(), () => false);
        EchoOrchestrator orchestrator = CreateOrchestrator(activity, monitor);
        await StartAsync(orchestrator);

        using var cancelled = new SemaphoreSlim(0);
        monitor.OnRecordingCancelled += (_, _) => cancelled.Release();

        listener.Raise(l => l.Pressed += null, new HotkeyEventArgs());
        Assert.True(handling.Wait(_patience));

        // Act: the desktop takes the key back before Echo has even started to record
        listener.Raise(l => l.Lost += null, EventArgs.Empty);
        bool isLossHandledFirst = await cancelled.WaitAsync(TimeSpan.FromMilliseconds(100));
        handled.Set();

        // Assert: the loss waits for the press, so it ends the recording the press starts, and the screen does not
        // stay at "recording" with a key that will never come up
        Assert.False(isLossHandledFirst);
        Assert.True(await stopped.WaitAsync(_patience));

        await orchestrator.StopAsync(CancellationToken.None);

        _audioMock.Verify(a => a.StartRecording(), Times.Once);
        Assert.Equal(ListenState.Listening, activity.State);
    }

    [Fact]
    public async Task SessionReturned_UserBackFromAnotherAccount_OpensTheMicrophoneAndTheSoundsAgain()
    {
        // Arrange
        await StartAsync(_orchestrator);

        // Act: stopping waits for the opening, which runs in the background
        _sessionMock.Raise(s => s.Returned += null, EventArgs.Empty);
        await _orchestrator.StopAsync(CancellationToken.None);

        // Assert
        _audioMock.Verify(a => a.ReopenMicrophone(), Times.Once);
        _soundMock.Verify(s => s.ReopenSounds(), Times.Once);
    }

    [Fact]
    public async Task SessionReturned_EchoAlreadyStopped_OpensNothing()
    {
        // Arrange
        await StartAsync(_orchestrator);
        await _orchestrator.StopAsync(CancellationToken.None);

        // Act
        _sessionMock.Raise(s => s.Returned += null, EventArgs.Empty);

        // Assert
        _audioMock.Verify(a => a.ReopenMicrophone(), Times.Never);
        _soundMock.Verify(s => s.ReopenSounds(), Times.Never);
    }

    [Fact]
    public async Task MicrophonesChanged_NothingRecorded_OpensOnlyTheMicrophoneAgain()
    {
        // Arrange
        await StartAsync(_orchestrator);

        // Act: stopping waits for the opening, which runs in the background
        _devicesMock.Raise(d => d.MicrophonesChanged += null, EventArgs.Empty);
        await _orchestrator.StopAsync(CancellationToken.None);

        // Assert
        _audioMock.Verify(a => a.ReopenMicrophone(), Times.Once);
        _soundMock.Verify(s => s.ReopenSounds(), Times.Never);
    }

    [Fact]
    public async Task LoudspeakersChanged_OpensOnlyTheSoundsAgain()
    {
        // Arrange
        await StartAsync(_orchestrator);

        // Act
        _devicesMock.Raise(d => d.LoudspeakersChanged += null, EventArgs.Empty);
        await _orchestrator.StopAsync(CancellationToken.None);

        // Assert
        _soundMock.Verify(s => s.ReopenSounds(), Times.Once);
        _audioMock.Verify(a => a.ReopenMicrophone(), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MicrophonesChanged_WhileAPhraseIsRecorded_OpensTheMicrophoneOnlyOnceTheRecordingEnds(bool isKeyLost)
    {
        // Arrange: the openings run one after another, so the sounds, opened after the microphones changed,
        // show when everything queued before them has run
        using var soundsOpened = new SemaphoreSlim(0);
        _soundMock.Setup(s => s.ReopenSounds()).Callback(() => soundsOpened.Release());

        var calls = new ConcurrentQueue<string>();
        _audioMock.Setup(a => a.StopRecording()).Callback(() => calls.Enqueue("stop")).Returns((MemoryStream?)null);
        _audioMock.Setup(a => a.ReopenMicrophone()).Callback(() => calls.Enqueue("reopen"));

        await StartAsync(_orchestrator);
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);

        // Act
        _devicesMock.Raise(d => d.MicrophonesChanged += null, EventArgs.Empty);
        _devicesMock.Raise(d => d.LoudspeakersChanged += null, EventArgs.Empty);
        Assert.True(await soundsOpened.WaitAsync(_patience));
        bool isOpenedWhileRecording = !calls.IsEmpty;

        if (isKeyLost)
            _monitorMock.Raise(m => m.OnRecordingCancelled += null, EventArgs.Empty);
        else
            _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);

        await _orchestrator.StopAsync(CancellationToken.None);

        // Assert: the phrase was not cut, and the microphone was opened again as soon as it ended
        Assert.False(isOpenedWhileRecording);
        Assert.Equal(["stop", "reopen"], calls.ToArray());
    }

    [Fact]
    public async Task OnRecordingTriggered_MicrophoneDoesNotOpen_RecordsNothingAndStaysReady()
    {
        // Arrange: the microphone was unplugged
        _audioMock.Setup(a => a.StartRecording()).Returns(false);

        var activity = new ListenActivity(new ScreenLog(), () => false);
        EchoOrchestrator orchestrator = CreateOrchestrator(activity);
        await StartAsync(orchestrator);

        // Act
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);
        bool isRecording = activity.State == ListenState.Recording;

        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);
        await orchestrator.StopAsync(CancellationToken.None);

        // Assert: the screen never said it records, and there was nothing to recognize
        Assert.False(isRecording);
        _audioMock.Verify(a => a.StopRecording(), Times.Never);
        _whisperMock.Verify(w => w.ProcessAudioAsync(It.IsAny<MemoryStream?>()), Times.Never);
        Assert.Equal(ListenState.Listening, activity.State);
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

    private EchoOrchestrator CreateOrchestrator(IListenActivity activity, IPushToTalkMonitorService? monitor = null) =>
        new(
            _loggerMock.Object,
            _soundMock.Object,
            _textMock.Object,
            _audioMock.Object,
            _whisperMock.Object,
            monitor ?? _monitorMock.Object,
            Mock.Of<IErrorWindow>(),
            activity);

    /// <summary>
    ///     The monitor itself, for the tests of how the notices of the key and Echo meet.
    /// </summary>
    private PushToTalkMonitorService CreateMonitor(Mock<IHotkeyListener> listener) =>
        new(listener.Object,
            _soundMock.Object,
            Mock.Of<ILogger<PushToTalkMonitorService>>(),
            Options.Create(new PushToTalkSettings { PlaySound = false }));
}