using System.Globalization;
using System.Reflection;
using Echo.Listening;
using Microsoft.Extensions.Hosting;
using Whisper.net.LibraryLoader;

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
internal sealed class EchoOrchestrator(
    ILogger<EchoOrchestrator> logger,
    ISoundPlayService soundPlayService,
    ITextInsertionService textInsertionService,
    IAudioRecordingService audioRecordingService,
    IWhisperInferenceService whisperInferenceService,
    IPushToTalkMonitorService pushToTalkMonitorService,
    IErrorWindow errorWindow,
    IListenActivity? activity = null) : BackgroundService
{
    private readonly IListenActivity? _activity = activity;

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

    private bool _isStopping;
    private bool _isRecording;
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

            _whisperInferenceService.TryInitializeWhisperModel();

            _soundPlayService.EnsureCanPlaySounds();

            // Last, so that the key's heading is followed by what the keyboard part says when it starts
            _pushToTalkMonitorService.InitializePushToTalkHotkeysOrThrow();

            lock (_stateLock)
            {
                // Stopped while starting: the key is never listened to
                if (_isStopping)
                    throw new OperationCanceledException(stoppingToken);

                _pushToTalkMonitorService.OnRecordingTriggered += HandlePushToTalkTriggered;
                _pushToTalkMonitorService.OnRecordingReleased += HandlePushToTalkReleased;
            }

            await _pushToTalkMonitorService.StartListeningAsync();

            if (_activity is not null)
            {
                _activity.Runtime = RuntimeName(RuntimeOptions.LoadedLibrary);
                _activity.SetState(ListenState.Listening);
            }

            Log.Information(
                "Ech{EarEmoji} is ready and running.",
                LoggerConstants.EarEmoji);
        }
        catch (OperationCanceledException) when (_isStopping)
        {
            // Stopped before it was ready: nothing failed
        }
        catch (Exception exc)
        {
            _logger.LogError("{ErrorMessage}", exc.Message);

            // On the working screen the reason stays in sight, and the user can change the settings from there
            if (_activity is not null)
            {
                _activity.SetState(ListenState.Failed);
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

        _logger.LogInformation("{BlackSquareButtonEmoji} Recording...",
            LoggerConstants.BlackSquareButtonEmoji);

        lock (_stateLock)
        {
            if (_isStopping)
                return;

            _isRecording = true;
            ShowState();

            _audioRecordingService.StartRecording();
        }
    }

    private void HandlePushToTalkReleased(object? sender, EventArgs e)
    {
        string seconds = Stopwatch.GetElapsedTime(_pressedAt).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture);

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

            Task recognition = Task.Run(() => RecognizeAsync(audioStream));
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
    ///     Recognizes the phrase just said, then tells the screen whether anything is still under way.
    /// </summary>
    private async Task RecognizeAsync(MemoryStream? audioStream)
    {
        try
        {
            await ProcessRecordingPayloadAsync(audioStream);
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
    ///     recognized while the next one is said leaves the state at "recording".
    /// </summary>
    private void ShowState() =>
        _activity?.SetState(_isRecording ? ListenState.Recording
            : _waitingPhrases > 0 ? ListenState.Transcribing
            : ListenState.Listening);

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

        Task[] recognitions;

        lock (_stateLock)
            recognitions = [.. _recognitions];

        // The phrases already said are finished, however long that takes: the model cannot be unloaded
        // in the middle of one, and what the user said is not lost
        await Task.WhenAll(recognitions);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _pushToTalkMonitorService.OnRecordingTriggered -= HandlePushToTalkTriggered;
        _pushToTalkMonitorService.OnRecordingReleased -= HandlePushToTalkReleased;

        _phraseTurn.Dispose();

        base.Dispose();
    }

    /// <summary>
    ///     Recognizes a finished recording and types the text, one phrase at a time.
    /// </summary>
    /// <param name="audioStream">The recording, taken from the microphone when the key was let go.</param>
    internal async Task ProcessRecordingPayloadAsync(MemoryStream? audioStream)
    {
        try
        {
            await _phraseTurn.WaitAsync();

            try
            {
                Stopwatch stopwatch = Stopwatch.StartNew();

                string? textFromAudio = await _whisperInferenceService.ProcessAudioAsync(audioStream);

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