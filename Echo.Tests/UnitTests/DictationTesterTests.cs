namespace Echo.Tests.UnitTests;

public sealed class DictationTesterTests : IDisposable
{
    /// <summary>
    ///     How long a test waits for what it holds up. A fix that breaks the order fails the test instead of hanging it.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly SetupContext _draft = SetupContext.FromSettings(new UserSettings());
    private readonly List<string> _events = [];

    public DictationTesterTests()
    {
        Directory.CreateDirectory(_directory);

        _paths = new AppPaths(_directory, _directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ToOverrides_AdditionalSettingsOfTheDraft_AreWhatTheTestRunsWith()
    {
        // Arrange: saved settings, then a draft that changes one of them and sets another back
        new UserSettingsStore(_paths).Save(new UserSettings
        {
            Additional = new AdditionalValues()
                .With(AdditionalSettings.Threads, "4")
                .With(AdditionalSettings.Prompt, "old words"),
        });

        SetupContext draft = SetupContext.FromSettings(new UserSettings
        {
            Additional = new AdditionalValues().With(AdditionalSettings.Threads, "8"),
        });

        // Act
        IConfiguration configuration = ServiceCollectionExtensions.BuildConfiguration(_paths, DictationTester.ToOverrides(draft));

        // Assert
        WhisperSettings whisper = configuration.GetSection(nameof(WhisperSettings)).Get<WhisperSettings>()!;

        Assert.Equal(8, whisper.Threads);
        Assert.Null(whisper.Prompt);

        // A test never beeps and never types into another window
        Assert.Equal(bool.FalseString, configuration["PushToTalkSettings:PlaySound"]);
        Assert.Equal(bool.FalseString, configuration["TextInsertionSettings:UseAutoInsert"]);
    }

    [Fact]
    public async Task StartAsync_MicrophoneThatDoesNotAnswer_TheNextAttemptOpensOneOnlyOnceItHasLetGo()
    {
        // Arrange: the first microphone answers only when the test lets it
        using var firstAnswers = new ManualResetEventSlim();
        int built = 0;

        var tester = new DictationTester(
            _ =>
            {
                int attempt = Interlocked.Increment(ref built);

                return Services(attempt, attempt == 1 ? firstAnswers : null);
            },
            startTimeout: TimeSpan.FromMinutes(1),
            deviceTimeout: TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<DictationTestException>(() => tester.StartAsync(_draft, CancellationToken.None));

        // Act: try again while the first microphone is still being opened
        Task<IDictationTestSession> second = tester.StartAsync(_draft, CancellationToken.None);

        firstAnswers.Set();
        await using IDictationTestSession session = await second;

        // Assert: the first microphone was closed before the next one was asked for
        Assert.Equal(["open 1", "close 1", "open 2"], Events());
    }

    [Fact]
    public async Task StartAsync_AttemptGivenUpOnWhileItWaited_NeverOpensAnything()
    {
        // Arrange: the first microphone answers only when the test lets it, and the first attempt is given up on
        using var firstAnswers = new ManualResetEventSlim();
        int built = 0;

        var tester = new DictationTester(
            _ =>
            {
                int attempt = Interlocked.Increment(ref built);

                return Services(attempt, attempt == 1 ? firstAnswers : null);
            },
            startTimeout: TimeSpan.FromSeconds(1),
            deviceTimeout: TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<DictationTestException>(() => tester.StartAsync(_draft, CancellationToken.None));

        // Act: a second attempt runs out of time while it waits for the first, then a third one is made
        await Assert.ThrowsAsync<DictationTestException>(() => tester.StartAsync(_draft, CancellationToken.None));

        Task<IDictationTestSession> third = tester.StartAsync(_draft, CancellationToken.None);

        firstAnswers.Set();
        await using IDictationTestSession session = await third;

        // Assert: the second attempt took nothing; the third one opened its microphone after the first let go
        Assert.Equal(2, built);
        Assert.Equal(["open 1", "close 1", "open 2"], Events());
    }

    [Fact]
    public async Task DisposeAsync_PhraseStillRecognized_IsWaitedForBeforeTheServicesGo()
    {
        // Arrange: a session whose phrase is still being recognized
        var phrase = new TaskCompletionSource<string?>();
        var whisper = new Mock<IWhisperInferenceService>();
        var monitor = new Mock<IPushToTalkMonitorService>();

        whisper.Setup(w => w.ProcessAudioAsync(It.IsAny<MemoryStream?>())).Returns(phrase.Task);

        var tester = new DictationTester(
            _ => Services(attempt: 1, answers: null, whisper.Object, monitor.Object),
            startTimeout: TimeSpan.FromMinutes(1),
            deviceTimeout: TimeSpan.FromMinutes(1));

        IDictationTestSession session = await tester.StartAsync(_draft, CancellationToken.None);

        monitor.Raise(m => m.OnRecordingReleased += null, EventArgs.Empty);

        // Act
        Task closing = session.DisposeAsync().AsTask();
        bool isClosedTooEarly = closing.IsCompleted;

        phrase.SetResult("hello");
        await closing.WaitAsync(_patience);

        // Assert: the microphone and the model went only after the phrase was done
        Assert.False(isClosedTooEarly);
        whisper.Verify(w => w.ProcessAudioAsync(It.IsAny<MemoryStream?>()), Times.Once);
        Assert.Equal(["open 1", "close 1"], Events());
    }

    /// <summary>
    ///     The services of a test session, with a microphone that notes when it is opened and closed.
    ///     When <paramref name="answers"/> is given, the microphone opens only once it is set.
    /// </summary>
    private ServiceProvider Services(int attempt,
                                     ManualResetEventSlim? answers,
                                     IWhisperInferenceService? whisper = null,
                                     IPushToTalkMonitorService? monitor = null)
    {
        var audio = new Mock<IAudioRecordingService>();

        audio.Setup(a => a.InititalizeAudioRecordingCapabilities()).Callback(() =>
        {
            Note($"open {attempt}");

            if (answers is not null && !answers.Wait(_patience))
                throw new TimeoutException("The test never let the microphone answer.");
        });

        audio.Setup(a => a.Dispose()).Callback(() => Note($"close {attempt}"));

        var services = new ServiceCollection();

        // Made by factories, so the provider closes them the way it closes real ones
        services.AddSingleton(_ => audio.Object);
        services.AddSingleton(_ => whisper ?? Mock.Of<IWhisperInferenceService>());
        services.AddSingleton(_ => monitor ?? Mock.Of<IPushToTalkMonitorService>());

        return services.BuildServiceProvider();
    }

    private void Note(string happened)
    {
        lock (_events)
            _events.Add(happened);
    }

    private string[] Events()
    {
        lock (_events)
            return [.. _events];
    }
}