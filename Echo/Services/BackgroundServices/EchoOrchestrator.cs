namespace Echo.Services.BackgroundServices;

/// <summary>
///     The central background service that is responsible for bootstrapping hardware initialization, loading AI models, 
///     and orchestrating the flow between voice capture and text inference.
/// </summary>
/// <param name="pushToTalkMonitorService"><see cref="AssetsProvider"/></param>
/// <param name="logger"><see cref="ILogger{EchoOrchestrator}"/></param>
/// <param name="audioRecordingService"><see cref="SoundPlayService"/></param>
/// <param name="soundPlayService"><see cref="AudioRecordingService"/></param>
/// <param name="textInsertionService"><see cref="WhisperInferenceService"/></param>
/// <param name="whisperInferenceService"><see cref="PushToTalkMonitorService"/></param>
/// <param name="errorWindow">Shows an error that stops Echo when there is no working screen to show it on.</param>
/// <param name="activity">The working screen, when Echo runs with one.</param>
/// <param name="sessionEvents">Says when the user comes back to this session, where the system tells about that.</param>
/// <param name="deviceEvents">Says when the sound devices change, where the system tells about that.</param>
/// <param name="memory">Keeps the recognized phrases, when there is a dictation history.</param>
internal sealed class EchoOrchestrator(
    ILogger<EchoOrchestrator> logger,
    ISoundPlayService soundPlayService,
    ITextInsertionService textInsertionService,
    IAudioRecordingService audioRecordingService,
    IWhisperInferenceService whisperInferenceService,
    IPushToTalkMonitorService pushToTalkMonitorService,
    IErrorWindow errorWindow,
    IListenActivity? activity = null,
    ISessionEvents? sessionEvents = null,
    IAudioDeviceEvents? deviceEvents = null,
    IDictationMemory? memory = null) : BackgroundService
{
    /// <summary>
    ///     What the recordings are: 16 kHz, 16 bits, one channel, after a header of 44 bytes.
    /// </summary>
    private const int WavHeaderBytes = 44;

    /// <summary>
    ///     The bytes in a second of the microphone's 16 kHz, 16-bit mono samples.
    /// </summary>
    private const int AudioBytesPerSecond = 16000 * 2;

    private readonly IListenActivity? _activity = activity;
    private readonly ISessionEvents? _sessionEvents = sessionEvents;
    private readonly IAudioDeviceEvents? _deviceEvents = deviceEvents;
    private readonly IDictationMemory? _memory = memory;

    private readonly ILogger<EchoOrchestrator> _logger = logger;
    private readonly ISoundPlayService _soundPlayService = soundPlayService;
    private readonly ITextInsertionService _textInsertionService = textInsertionService;
    private readonly IAudioRecordingService _audioRecordingService = audioRecordingService;
    private readonly IWhisperInferenceService _whisperInferenceService = whisperInferenceService;
    private readonly IPushToTalkMonitorService _pushToTalkMonitorService = pushToTalkMonitorService;
    private readonly IErrorWindow _errorWindow = errorWindow;

    /// <summary>
    ///     One phrase at a time, from recognition to insertion: the model mixes up the text of phrases it is
    ///     given together, and the next phrase must not take the clipboard before the previous one is pasted.
    /// </summary>
    private readonly SemaphoreSlim _phraseTurn = new(1, 1);

    /// <summary>
    ///     Guards everything below: the key handlers, the start-up and stopping all go through it,
    ///     so stopping knows exactly which phrases were accepted before it.
    /// </summary>
    private readonly object _stateLock = new();

    /// <summary>
    ///     The phrases that are still being recognized. Echo stops only once they are done,
    ///     since the model cannot be unloaded in the middle of one.
    /// </summary>
    private readonly HashSet<Task> _recognitions = [];

    /// <summary>
    ///     Whether everything a phrase needs is there: the model loaded and the pasting prepared. True once the
    ///     start-up is through, false when it failed or Echo stopped before it was through.
    /// </summary>
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     The opening of the microphone and the loudspeakers again after a return to the session or a change of the
    ///     devices, the latest one queued after those before it. Stopping waits for it, so that nothing is opened after
    ///     the services closed it.
    /// </summary>
    private Task _reopening = Task.CompletedTask;

    /// <summary>
    ///     Whether the microphone is to be opened again once the key held now comes up: opened again in the middle of
    ///     a phrase, it would cut it.
    /// </summary>
    private bool _reopensMicrophoneAfterRecording;

    private bool _isStopping;
    private bool _isRecording;
    private bool _isReady;
    private bool _hasFailed;
    private int _waitingPhrases;
    private long _pressedAt;

    /// <inheritdoc/>
    /// <param name="stoppingToken"><see cref="CancellationToken"/></param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Log.Information(
                "Ech{EarEmoji} {Version} is checking dependencies...",
                LoggerConstants.EarEmoji,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));

            _audioRecordingService.InititalizeAudioRecordingCapabilities();

            _soundPlayService.EnsureCanPlaySounds();

            // The key is taken before the model, which takes the longest to load: pressed meanwhile, it records
            // instead of being typed into Echo's own window, and the phrase waits for the model
            _pushToTalkMonitorService.InitializePushToTalkHotkeysOrThrow();

            lock (_stateLock)
            {
                // Stopped while starting: the key is never listened to
                if (_isStopping)
                    throw new OperationCanceledException(stoppingToken);

                _pushToTalkMonitorService.OnRecordingTriggered += HandlePushToTalkTriggered;
                _pushToTalkMonitorService.OnRecordingReleased += HandlePushToTalkReleased;
                _pushToTalkMonitorService.OnRecordingCancelled += HandlePushToTalkCancelled;

                if (_sessionEvents is not null)
                    _sessionEvents.Returned += HandleSessionReturned;

                if (_deviceEvents is not null)
                {
                    _deviceEvents.MicrophonesChanged += HandleMicrophonesChanged;
                    _deviceEvents.LoudspeakersChanged += HandleLoudspeakersChanged;
                }
            }

            // Both may wait for the user to answer a window of the desktop; stopping Echo stops the waiting
            await _pushToTalkMonitorService.StartListeningAsync(stoppingToken);

            _whisperInferenceService.TryInitializeWhisperModel();

            await _textInsertionService.PrepareAsync(stoppingToken);

            lock (_stateLock)
            {
                _isReady = true;

                if (_activity is not null)
                    _activity.Runtime = RuntimeName(RuntimeOptions.LoadedLibrary);

                // A phrase said while the model loaded may still be recording or waiting: the state says so
                ShowState();
            }

            _ready.TrySetResult(true);

            Log.Information(
                "Ech{EarEmoji} is ready and running.",
                LoggerConstants.EarEmoji);
        }
        catch (OperationCanceledException) when (_isStopping)
        {
            // Stopped before it was ready: nothing failed
            _ready.TrySetResult(false);
        }
        catch (Exception exc)
        {
            _logger.LogError("{ErrorMessage}", exc.Message);

            // The key may already be Echo's: it goes back to doing what it does without Echo, and a phrase it is
            // recording is dropped
            await _pushToTalkMonitorService.ReleaseAsync();

            lock (_stateLock)
            {
                _hasFailed = true;
                ShowState();
            }

            _ready.TrySetResult(false);

            // On the working screen the reason stays in sight, and the user can change the settings from there
            if (_activity is not null)
            {
                _activity.Log.Add(EntryKind.Answer, Strings.ListenFailedHint);

                return;
            }

            _errorWindow.Show(exc.Message);

            Environment.Exit(1);
        }
    }

    private static string? RuntimeName(RuntimeLibrary? library) => library switch
    {
        null => null,
        RuntimeLibrary.Cuda => "CUDA",
        RuntimeLibrary.Vulkan => "Vulkan",
        _ => Strings.AccelerationCpu
    };

    private void HandlePushToTalkTriggered(object? sender, EventArgs e)
    {
        _pressedAt = Stopwatch.GetTimestamp();

        lock (_stateLock)
        {
            if (_isStopping)
                return;

            // Without its microphone the key records nothing: the screen has said why
            if (!_audioRecordingService.StartRecording())
                return;

            _isRecording = true;
            ShowState();
        }

        _logger.LogInformation("{BlackSquareButtonEmoji} Recording...",
            LoggerConstants.BlackSquareButtonEmoji);
    }

    private void HandlePushToTalkReleased(object? sender, EventArgs e)
    {
        lock (_stateLock)
        {
            // The press recorded nothing: Echo was stopping, or the microphone did not open
            if (!_isRecording)
                return;
        }

        TimeSpan held = Stopwatch.GetElapsedTime(_pressedAt);
        string seconds = held.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

        MemoryStream? audioStream = _audioRecordingService.StopRecording();

        // A recording without sound is not recognized, so its line does not say it is
        if (WhisperInferenceService.IsAudioSilentOrEmpty(audioStream))
        {
            _logger.LogInformation("{WhiteSquareButtonEmoji} Recorded {Seconds} s.",
                LoggerConstants.WhiteSquareButtonEmoji, seconds);
        }
        else
        {
            _logger.LogInformation("{WhiteSquareButtonEmoji} Recorded {Seconds} s, recognizing...",
                LoggerConstants.WhiteSquareButtonEmoji, seconds);
        }

        lock (_stateLock)
        {
            // A phrase that ends while Echo stops is not taken any more: stopping waits only for what it accepted
            if (_isStopping)
                return;

            _isRecording = false;
            _waitingPhrases++;
            ShowState();

            if (_reopensMicrophoneAfterRecording)
                ReopenMicrophone();

            Task recognition = Task.Run(() => RecognizeAsync(audioStream, held));
            _recognitions.Add(recognition);

            _ = recognition.ContinueWith(
                done =>
                {
                    lock (_stateLock)
                        _recognitions.Remove(done);
                },
                TaskScheduler.Default);
        }
    }

    /// <summary>
    ///     The key stopped reaching Echo while it was held, lost or given back: the recording ends, and what it holds
    ///     is dropped rather than typed into a window nobody expects it in.
    /// </summary>
    private void HandlePushToTalkCancelled(object? sender, EventArgs e)
    {
        lock (_stateLock)
        {
            if (_isStopping || !_isRecording)
                return;
        }

        // Ended before it stops counting as recording, as when the key comes up: an opening of the microphone that
        // waits for the end of the recording then never finds it still going
        _audioRecordingService.StopRecording();

        lock (_stateLock)
        {
            _isRecording = false;
            ShowState();

            if (_reopensMicrophoneAfterRecording)
                ReopenMicrophone();
        }

        _logger.LogWarning("{WarningEmoji} The push-to-talk key stopped reaching Echo while recording; the phrase is dropped.",
            LoggerConstants.WarningEmoji);
    }

    /// <summary>
    ///     The user is back after another account or a remote connection had the computer. The system closed the
    ///     microphone and the loudspeakers of this session meanwhile, and Echo would go on hearing nothing.
    /// </summary>
    private void HandleSessionReturned(object? sender, EventArgs e)
    {
        _logger.LogInformation("{StudioMicrophoneEmoji} Back in this session: opening the microphone and the sounds again.",
            LoggerConstants.StudioMicrophoneEmoji);

        // Marked at once: a recording that begins before the opening runs opens the microphone again itself
        _audioRecordingService.MarkMicrophoneForReopening();

        lock (_stateLock)
        {
            ReopenMicrophone();
            ReopenSounds();
        }
    }

    /// <summary>
    ///     A microphone was plugged in or out, or came back after the computer slept. The system closed the sound of
    ///     the one that went away, and Echo would go on hearing nothing from it even once it is back.
    /// </summary>
    private void HandleMicrophonesChanged(object? sender, EventArgs e)
    {
        _logger.LogDebug("{StudioMicrophoneEmoji} The microphones of the system changed: opening the microphone again.",
            LoggerConstants.StudioMicrophoneEmoji);

        // Marked at once: a recording that begins before the opening runs opens the microphone again itself
        _audioRecordingService.MarkMicrophoneForReopening();

        lock (_stateLock)
            ReopenMicrophone();
    }

    /// <summary>
    ///     Loudspeakers went away or came back, or the system plays to other ones by default: the sounds go where the
    ///     system plays now.
    /// </summary>
    private void HandleLoudspeakersChanged(object? sender, EventArgs e)
    {
        _logger.LogDebug("{NoteEmoji} The loudspeakers of the system changed: opening the sounds again.",
            LoggerConstants.NoteEmoji);

        lock (_stateLock)
            ReopenSounds();
    }

    /// <summary>
    ///     Queues the opening of the microphone again. Called under <see cref="_stateLock"/>.
    /// </summary>
    private void ReopenMicrophone()
    {
        _reopensMicrophoneAfterRecording = false;

        if (!_isStopping)
            Reopen(ReopenMicrophoneOutsideRecording);
    }

    /// <summary>
    ///     Opens the microphone again unless a recording is under way, which it would cut: then once the key comes up.
    ///     Looked at when the opening runs, not when it was queued, since a key pressed meanwhile starts a recording.
    /// </summary>
    private void ReopenMicrophoneOutsideRecording()
    {
        lock (_stateLock)
        {
            if (_isRecording)
            {
                _reopensMicrophoneAfterRecording = true;

                return;
            }
        }

        if (_audioRecordingService.ReopenMicrophone())
            return;

        // A recording began between the look above and the opening, and the service left it whole. The microphone
        // stays marked: the end of that recording opens it again, or, if it has already ended, the next recording
        lock (_stateLock)
        {
            if (_isRecording)
                _reopensMicrophoneAfterRecording = true;
        }
    }

    /// <summary>
    ///     Opens the loudspeakers again. Called under <see cref="_stateLock"/>.
    /// </summary>
    private void ReopenSounds()
    {
        if (!_isStopping)
            Reopen(_soundPlayService.ReopenSounds);
    }

    /// <summary>
    ///     Queues an opening after those before it. The notices come on threads of the system, which are not kept
    ///     waiting for the devices. Called under <see cref="_stateLock"/>.
    /// </summary>
    private void Reopen(Action opening) =>
        _reopening = _reopening.ContinueWith(_ => opening(), TaskScheduler.Default);

    /// <summary>
    ///     Recognizes the phrase just said, then tells the screen whether anything is still under way.
    /// </summary>
    /// <param name="held">How long the key was held.</param>
    private async Task RecognizeAsync(MemoryStream? audioStream, TimeSpan held)
    {
        try
        {
            // A phrase said while the model was loading waits for it
            if (await _ready.Task)
            {
                await ProcessRecordingPayloadAsync(audioStream, held);
            }
            else
            {
                _logger.LogWarning("{WarningEmoji} The phrase is dropped: Echo did not get ready to recognize it.",
                    LoggerConstants.WarningEmoji);
            }
        }
        finally
        {
            lock (_stateLock)
            {
                _waitingPhrases--;
                ShowState();
            }
        }
    }

    /// <summary>
    ///     What the screen shows follows everything under way, not the latest event: a phrase that is
    ///     recognized while the next one is said leaves the state at "recording", and a phrase said while the model
    ///     is still loading goes back to "starting", not to "ready".
    /// </summary>
    private void ShowState() =>
        _activity?.SetState(_hasFailed ? ListenState.Failed
            : _isRecording ? ListenState.Recording
            : _waitingPhrases > 0 ? ListenState.Transcribing
            : _isReady ? ListenState.Listening
            : ListenState.Starting);

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // From here on no phrase is taken and the start-up does not listen to the key; a key handler that is
        // already inside has either registered its phrase or will find Echo stopping
        lock (_stateLock)
            _isStopping = true;

        try
        {
            await base.StopAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The host has run out of patience; the start-up is still waited for below
        }

        // The host gives up waiting for the start-up after a while, but its services are needed until it ends.
        // A start-up stopped before it began ends as cancelled, and has nothing to wait for
        if (ExecuteTask is not null)
            await ExecuteTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        _pushToTalkMonitorService.OnRecordingTriggered -= HandlePushToTalkTriggered;
        _pushToTalkMonitorService.OnRecordingReleased -= HandlePushToTalkReleased;
        _pushToTalkMonitorService.OnRecordingCancelled -= HandlePushToTalkCancelled;

        if (_sessionEvents is not null)
            _sessionEvents.Returned -= HandleSessionReturned;

        if (_deviceEvents is not null)
        {
            _deviceEvents.MicrophonesChanged -= HandleMicrophonesChanged;
            _deviceEvents.LoudspeakersChanged -= HandleLoudspeakersChanged;
        }

        Task[] recognitions;
        Task reopening;

        lock (_stateLock)
        {
            recognitions = [.. _recognitions];
            reopening = _reopening;
        }

        // The services close the devices once Echo has stopped: an opening under way ends before that
        await reopening.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // The phrases already said are finished, however long that takes: the model cannot be unloaded
        // in the middle of one, and what the user said is not lost
        await Task.WhenAll(recognitions);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _pushToTalkMonitorService.OnRecordingTriggered -= HandlePushToTalkTriggered;
        _pushToTalkMonitorService.OnRecordingReleased -= HandlePushToTalkReleased;
        _pushToTalkMonitorService.OnRecordingCancelled -= HandlePushToTalkCancelled;

        if (_sessionEvents is not null)
            _sessionEvents.Returned -= HandleSessionReturned;

        if (_deviceEvents is not null)
        {
            _deviceEvents.MicrophonesChanged -= HandleMicrophonesChanged;
            _deviceEvents.LoudspeakersChanged -= HandleLoudspeakersChanged;
        }

        _phraseTurn.Dispose();

        base.Dispose();
    }

    /// <summary>
    ///     Recognizes a finished recording and types the text, one phrase at a time.
    /// </summary>
    /// <param name="audioStream">The recording, taken from the microphone when the key was let go.</param>
    /// <param name="held">How long the key was held.</param>
    internal async Task ProcessRecordingPayloadAsync(MemoryStream? audioStream, TimeSpan held = default)
    {
        try
        {
            await _phraseTurn.WaitAsync();

            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                // Measured before recognition, which may leave the stream behind it
                TimeSpan audio = TimeSpan.FromSeconds(Math.Max(0, (audioStream?.Length ?? 0) - WavHeaderBytes) / (double)AudioBytesPerSecond);

                string? textFromAudio = await _whisperInferenceService.ProcessAudioAsync(audioStream);
                stopwatch.Stop();

                // Kept before it is typed, so a phrase whose typing fails is kept all the same
                if (!string.IsNullOrWhiteSpace(textFromAudio))
                {
                    try
                    {
                        _memory?.Add(textFromAudio, held, audio, stopwatch.Elapsed);
                    }
                    catch (OutOfMemoryException)
                    {
                        // The optional history must not lose an already recognized phrase when memory is tight.
                        // Logging here could itself need memory; the phrase still goes to the screen and insertion.
                    }
                }

                if (_activity is not null && !string.IsNullOrWhiteSpace(textFromAudio))
                {
                    _activity.Log.Add(EntryKind.Dictated, textFromAudio, Strings.Format(nameof(Strings.TestSeconds), Echo.UI.Format.Seconds(stopwatch.ElapsedMilliseconds)));

                    // Typed into Echo's own window, the text would land in its command line: there it is only shown
                    if (_activity.IsEchoFocused())
                        return;
                }

                await _textInsertionService.InsertTextAsync(textFromAudio);
            }
            finally
            {
                _phraseTurn.Release();
            }
        }
        catch (Exception exc)
        {
            _logger.LogError(exc, "{ExplosionEmoji} Error: {ErrorMessage}",
                LoggerConstants.ExplosionEmoji, exc.Message);
        }
    }
}