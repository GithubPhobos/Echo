using Echo.Services.BackgroundServices;

namespace Echo.Tests.UnitTests;

public sealed class EchoOrchestratorTests
{
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
            _monitorMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_InitializesAllDependencies()
    {
        // Act
        await _orchestrator.StartAsync(CancellationToken.None);

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
        await _orchestrator.StartAsync(CancellationToken.None);

        // Act
        _monitorMock.Raise(m => m.OnRecordingTriggered += null, EventArgs.Empty);

        // Assert
        _audioMock.Verify(a => a.StartRecording(), Times.Once);
    }

    [Fact]
    public async Task OnRecordingReleased_ProcessesAudioAndInsertsText()
    {
        // Arrange
        var fakeStream = new MemoryStream();
        string expectedText = "Hello, world!";

        _audioMock.Setup(a => a.StopRecording()).Returns(fakeStream);
        _whisperMock.Setup(w => w.ProcessAudioAsync(fakeStream)).ReturnsAsync(expectedText);

        await _orchestrator.StartAsync(CancellationToken.None);

        // Act
        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);

        // Method uses Task.Run, so we have to wait a bit
        await Task.Delay(100);

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

        await _orchestrator.StartAsync(CancellationToken.None);

        // Act
        _monitorMock.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);

        // Wait a bit for the Task.Run to execute
        await Task.Delay(100);

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
}