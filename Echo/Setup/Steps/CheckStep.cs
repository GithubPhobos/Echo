using Echo.Setup.Audio;
using Echo.Setup.Check;
using Echo.Setup.Hardware;
using Echo.Setup.Hotkeys;
using Echo.UI;
using Spectre.Console;

namespace Echo.Setup.Steps;

/// <summary>
///     Step 5: sound. The input device is chosen by watching which of them hears the user; its silence level
///     is measured on a page of its own, which says what that level is for; then the user dictates one phrase
///     through the real pipeline, with the model, backend and key picked on the previous steps.
/// </summary>
internal sealed class CheckStep : ISetupStep
{
    private const int MeterWidth = 40;
    private const int MeterRows = 3;

    private const int BriefNameLength = 20;
    private const string BriefCutMark = "...";

    private const string LevelFormat = "0.000";

    /// <summary>
    ///     A short phrase that takes longer than this to recognize means the model is too heavy for the machine.
    /// </summary>
    private const double SlowRecognitionMs = 2500;

    /// <summary>
    ///     What the silence level page is doing.
    /// </summary>
    private enum Phase
    {
        Explaining,
        MeasuringSilence,
        MeasuringVoice,
        VoiceNotHeard
    }

    /// <summary>
    ///     How the silence level page was left.
    /// </summary>
    private enum Measurement
    {
        Done,
        Skipped
    }

    private enum TestState
    {
        Waiting,
        Recording,
        Transcribing,
        Recognized,
        NothingHeard,
        Failed
    }

    private readonly SetupUi _ui;
    private readonly IMicrophoneProbe _microphoneProbe;
    private readonly IDictationTester _dictationTester;
    private readonly SetupTimings _timings;
    private readonly TimeProvider _time;

    /// <param name="ui"><see cref="SetupUi"/></param>
    /// <param name="microphoneProbe"><see cref="IMicrophoneProbe"/></param>
    /// <param name="dictationTester"><see cref="IDictationTester"/></param>
    /// <param name="timings"><see cref="SetupTimings"/></param>
    /// <param name="time">The clock the measurements and the waiting for a device go by.</param>
    public CheckStep(SetupUi ui, IMicrophoneProbe microphoneProbe, IDictationTester dictationTester, SetupTimings timings, TimeProvider time)
    {
        _ui = ui;
        _microphoneProbe = microphoneProbe;
        _dictationTester = dictationTester;
        _timings = timings;
        _time = time;

        Pages =
        [
            new SetupPage(ChooseMicrophoneAsync),
            new SetupPage(MeasureSilenceAsync, SubTitle: () => Strings.SubCalibration),
            new SetupPage(TestDictationAsync, SubTitle: () => Strings.SubDictationTest),
        ];
    }

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public string Title => Strings.StepCheck;

    /// <inheritdoc/>
    public IReadOnlyList<SetupPage> Pages { get; }

    /// <inheritdoc/>
    public IEnumerable<(string Label, string Value)> Summarize(SetupContext context)
    {
        yield return (Strings.SummaryMicrophone, context.MicrophoneName ?? string.Empty);
    }

    /// <inheritdoc/>
    public string? Brief(SetupContext context)
    {
        // Microphone names run long; the line of steps has room for the beginning of one
        if (context.MicrophoneName is not { Length: > 0 } name)
            return null;

        return name.Length <= BriefNameLength ? name : name[..BriefNameLength].TrimEnd() + BriefCutMark;
    }

    /// <summary>
    ///     The list of input devices with a live level meter for the highlighted one, so the user can move
    ///     between them and see which one reacts.
    /// </summary>
    private async Task<SetupStepResult> ChooseMicrophoneAsync(SetupContext context,
                                                              SetupScreen screen,
                                                              CancellationToken cancellationToken)
    {
        IReadOnlyList<MicrophoneDevice> devices;

        while ((devices = _microphoneProbe.GetDevices()).Count == 0)
        {
            UiResult<ConsoleKey> retry = await _ui.ShowAsync(
                screen,
                [_ui.Bad(Markup.Escape(Strings.MicrophoneNone)), string.Empty, SetupUi.Dim(Markup.Escape(Strings.MicrophoneNoneHelp))],
                [(ConsoleKey.Enter, Strings.HintCheckAgain)],
                cancellationToken);

            if (retry.IsBack)
                return SetupStepResult.Back;
        }

        int current = Math.Max(0, devices.ToList().FindIndex(device => device.Index == context.MicrophoneIndex));

        var recent = new Queue<float>(Enumerable.Repeat(0f, MeterWidth));

        using var listener = new MicrophoneListener(_microphoneProbe, _timings.DeviceTimeout, _time);
        listener.Switch(devices[current].Index);

        string[] hints = [$"{_ui.Theme.Arrows} {Strings.HintSelect}", $"Enter {Strings.HintConfirm}"];

        UiResult<bool> result = await _ui.PollAsync<bool>(
            screen,
            () =>
            {
                List<string> lines = [SetupUi.Question(Markup.Escape(Strings.MicrophoneQuestion)), string.Empty];

                lines.AddRange(devices.Select((device, index) => index == current
                    ? $"[bold {Theme.Highlight}]{_ui.Theme.Pointer}[/] [{Theme.Accent}]{_ui.Theme.RadioOn}[/] {SetupUi.Accent(Markup.Escape(device.Name))}"
                    : SetupUi.Dim($"{new string(' ', _ui.Theme.Pointer.Length)} {_ui.Theme.RadioOff} {Markup.Escape(device.Name)}")));

                lines.Add(string.Empty);
                lines.AddRange(DescribeLevel(listener, recent));

                return (lines, hints);
            },
            key =>
            {
                if (key?.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
                {
                    int next = (current + (key.Value.Key == ConsoleKey.UpArrow ? devices.Count - 1 : 1)) % devices.Count;

                    // With a single device there is nowhere to move: the microphone stays open as it is
                    if (next == current)
                        return PollStep<bool>.Continue;

                    current = next;
                    listener.Switch(devices[current].Index);

                    recent = new Queue<float>(Enumerable.Repeat(0f, MeterWidth));

                    return PollStep<bool>.Continue;
                }

                if (key?.Key == ConsoleKey.Enter)
                    return PollStep<bool>.Finish(UiResult<bool>.Done(true));

                if (key is null && listener.Poll() is IMicrophoneSession session)
                    Push(recent, session.ReadPeak());

                return PollStep<bool>.Continue;
            },
            cancellationToken);

        if (result.IsBack)
            return SetupStepResult.Back;

        // Another device has another silence level: the one measured before no longer applies
        if (context.MicrophoneIndex != devices[current].Index || context.MicrophoneName != devices[current].Name)
            context.SilenceThreshold = ThresholdCalculator.Default;

        context.MicrophoneIndex = devices[current].Index;
        context.MicrophoneName = devices[current].Name;

        return SetupStepResult.Next;
    }

    /// <summary>
    ///     Says what the silence level is for and measures it: first the room, then the user's voice.
    /// </summary>
    private async Task<SetupStepResult> MeasureSilenceAsync(SetupContext context,
                                                            SetupScreen screen,
                                                            CancellationToken cancellationToken)
    {
        while (true)
        {
            (UiResult<Measurement> outcome, Calibration calibration) = await ListenForLevelsAsync(context, screen, cancellationToken);

            if (outcome.IsBack)
                return SetupStepResult.Back;

            // Skipping keeps the level the microphone had before
            if (outcome.Value == Measurement.Skipped)
                return SetupStepResult.Next;

            context.SilenceThreshold = calibration.Threshold;

            UiResult<ConsoleKey> seen = await _ui.ShowAsync(
                screen,
                [
                    _ui.Good(SetupUi.Fill(Strings.CalibrationDone, Level(calibration.Threshold))),
                    string.Empty,
                    SetupUi.Dim(SetupUi.Fill(Strings.CalibrationDoneDetails, Level(calibration.NoiseLevel), Level(calibration.VoiceLevel))),
                ],
                [(ConsoleKey.Enter, Strings.HintContinue)],
                cancellationToken);

            // Escape from the result leads to the screen before it: the measurement, to be taken again
            if (!seen.IsBack)
                return SetupStepResult.Next;
        }
    }

    private async Task<(UiResult<Measurement> Outcome, Calibration Calibration)> ListenForLevelsAsync(SetupContext context,
                                                                                                    SetupScreen screen,
                                                                                                    CancellationToken cancellationToken)
    {
        Phase phase = Phase.Explaining;
        Calibration calibration = default;

        var quiet = new List<float>();
        var speech = new List<float>();
        var recent = new Queue<float>(Enumerable.Repeat(0f, MeterWidth));
        long measuringSince = 0;

        TimeSpan Measured() => _time.GetElapsedTime(measuringSince);

        using var listener = new MicrophoneListener(_microphoneProbe, _timings.DeviceTimeout, _time);
        listener.Switch(context.MicrophoneIndex);

        UiResult<Measurement> outcome = await _ui.PollAsync<Measurement>(
            screen,
            () =>
            {
                List<string> lines =
                [
                    SetupUi.Question(Markup.Escape(Strings.CalibrationQuestion)),
                    SetupUi.Dim(Markup.Escape(Strings.CalibrationWhy)),
                    SetupUi.Dim(Markup.Escape(Strings.CalibrationHow)),
                    string.Empty,
                    SetupUi.Dim(SetupUi.Fill(Strings.CalibrationCurrent, Level(context.SilenceThreshold))),
                    string.Empty,
                    .. DescribeLevel(listener, recent),
                    string.Empty,
                ];

                lines.AddRange(phase switch
                {
                    Phase.MeasuringSilence => [CountDown(Strings.CalibrationQuiet, _timings.QuietDuration - Measured()), string.Empty],
                    Phase.MeasuringVoice =>
                    [
                        CountDown(Strings.CalibrationSpeak, _timings.QuietDuration + _timings.SpeechDuration - Measured()),
                        string.Empty,
                    ],
                    Phase.VoiceNotHeard =>
                    [
                        _ui.Bad(Markup.Escape(Strings.CalibrationNotHeard)),
                        SetupUi.Dim(Markup.Escape(Strings.CalibrationNotHeardHelp)),
                    ],
                    _ => [string.Empty, string.Empty]
                });

                string[] hints = phase switch
                {
                    Phase.MeasuringSilence or Phase.MeasuringVoice => [$"S {Strings.HintSkip}"],
                    Phase.VoiceNotHeard => [$"Enter {Strings.HintTryAgain}", $"S {Strings.HintSkip}"],
                    _ when listener.Session is null => [$"S {Strings.HintSkip}"],
                    _ => [$"Enter {Strings.HintMeasure}", $"S {Strings.HintSkip}"]
                };

                return (lines, hints);
            },
            key =>
            {
                if (SetupUi.IsKey(key, ConsoleKey.S))
                    return PollStep<Measurement>.Finish(UiResult<Measurement>.Done(Measurement.Skipped));

                if (key?.Key == ConsoleKey.Enter && listener.Session is not null && phase is Phase.Explaining or Phase.VoiceNotHeard)
                {
                    phase = Phase.MeasuringSilence;
                    quiet.Clear();
                    speech.Clear();
                    measuringSince = _time.GetTimestamp();

                    return PollStep<Measurement>.Continue;
                }

                if (key is not null || listener.Poll() is not IMicrophoneSession session)
                    return PollStep<Measurement>.Continue;

                float level = session.ReadPeak();

                Push(recent, level);

                if (phase == Phase.MeasuringSilence)
                {
                    quiet.Add(level);

                    if (Measured() >= _timings.QuietDuration)
                        phase = Phase.MeasuringVoice;
                }
                else if (phase == Phase.MeasuringVoice)
                {
                    speech.Add(level);

                    if (Measured() >= _timings.QuietDuration + _timings.SpeechDuration)
                    {
                        calibration = ThresholdCalculator.Calculate(quiet, speech);

                        if (calibration.IsVoiceHeard)
                            return PollStep<Measurement>.Finish(UiResult<Measurement>.Done(Measurement.Done));

                        phase = Phase.VoiceNotHeard;
                    }
                }

                return PollStep<Measurement>.Continue;
            },
            cancellationToken);

        return (outcome, calibration);
    }

    /// <summary>
    ///     What the microphone hears right now, on the same number of rows whatever state it is in.
    /// </summary>
    private List<string> DescribeLevel(MicrophoneListener listener, Queue<float> recent)
    {
        List<string> lines;

        if (listener.Error is not null)
            lines = [_ui.Bad(Markup.Escape(Strings.MicrophoneFailed)), SetupUi.Dim(Markup.Escape(listener.Error))];
        else if (listener.Session is null)
            lines = [Markup.Escape(Strings.MicrophoneOpening)];
        else
            lines = [SetupUi.Dim(Markup.Escape(Strings.MicrophoneLevel)), .. _ui.Meter([.. recent], MeterRows)];

        while (lines.Count <= MeterRows)
            lines.Add(string.Empty);

        return lines;
    }

    /// <summary>
    ///     What the user is to do right now, with the whole seconds left to do it in.
    /// </summary>
    private static string CountDown(string text, TimeSpan left)
    {
        int seconds = Math.Max(1, (int)Math.Ceiling(left.TotalSeconds));

        return $"[bold {Theme.Highlight}]{SetupUi.Fill(text, seconds.ToString(System.Globalization.CultureInfo.CurrentUICulture))}[/]";
    }

    private static void Push(Queue<float> recent, float level)
    {
        recent.Dequeue();
        recent.Enqueue(level);
    }

    private static string Level(float level) =>
        level.ToString(LevelFormat, System.Globalization.CultureInfo.CurrentUICulture);

    private async Task<SetupStepResult> TestDictationAsync(SetupContext context,
                                                           SetupScreen screen,
                                                           CancellationToken cancellationToken)
    {
        while (true)
        {
            IDictationTestSession session;

            try
            {
                session = await _ui.WaitAsync(
                    screen,
                    Markup.Escape(Strings.TestLoading),
                    () => _dictationTester.StartAsync(context, cancellationToken),
                    cancellationToken);
            }
            catch (DictationTestException exc)
            {
                UiResult<ConsoleKey> failed = await _ui.ShowAsync(
                    screen,
                    [
                        _ui.Bad(Markup.Escape(Strings.TestStartFailed)),
                        string.Empty,
                        SetupUi.Dim(Markup.Escape(exc.Message)),
                        string.Empty,
                        SetupUi.Dim(Markup.Escape(Strings.TestStartFailedHelp)),
                    ],
                    [(ConsoleKey.Enter, Strings.HintTryAgain), (ConsoleKey.S, Strings.HintSkip)],
                    cancellationToken);

                if (failed.IsBack)
                    return SetupStepResult.Back;

                if (failed.Value == ConsoleKey.S)
                    return SetupStepResult.Next;

                continue;
            }

            await using (session)
            {
                UiResult<bool> finished = await ListenAsync(context, screen, session, cancellationToken);

                return finished.IsBack ? SetupStepResult.Back : SetupStepResult.Next;
            }
        }
    }

    private Task<UiResult<bool>> ListenAsync(SetupContext context,
                                             SetupScreen screen,
                                             IDictationTestSession session,
                                             CancellationToken cancellationToken)
    {
        TestState state = TestState.Waiting;
        string text = string.Empty;
        double elapsedMs = 0;
        int ticks = 0;

        string question = Strings.Format(nameof(Strings.TestQuestion), HotkeyNames.Display(context.Hotkey));

        return _ui.PollAsync<bool>(
            screen,
            () =>
            {
                List<string> lines = [SetupUi.Question(Markup.Escape(question)), string.Empty];

                lines.AddRange(state switch
                {
                    TestState.Recording => [$"[bold {Theme.Failure}]{_ui.Theme.Record} REC[/]"],
                    TestState.Transcribing => [Markup.Escape(Strings.TestTranscribing) + SetupUi.Dim(new string('.', ticks++ / 4 % 4))],
                    TestState.NothingHeard => [_ui.Bad(Markup.Escape(Strings.TestNothingHeard))],
                    TestState.Failed => [_ui.Bad(Markup.Escape(text))],
                    TestState.Recognized =>
                    [
                        $"{SetupUi.Dim(Markup.Escape(Strings.Format(nameof(Strings.TestSeconds), Format.Seconds(elapsedMs))))}  {Markup.Escape(text)}",

                        // Said only when the user has to do something about it
                        .. Acceleration.NeedsRestart(context.RequestedBackend, session.Runtime)
                            ? [string.Empty, SetupUi.Warning(Markup.Escape(Strings.Format(nameof(Strings.TestRestartNeeded), session.Runtime, context.RequestedBackend)))]
                            : Array.Empty<string>(),
                        .. elapsedMs > SlowRecognitionMs ? [string.Empty, SetupUi.Warning(Markup.Escape(Strings.TestSlow))] : Array.Empty<string>(),
                    ],
                    _ => [SetupUi.Dim(Markup.Escape(Strings.TestWaiting))]
                });

                // Finishing is offered only once a phrase has really gone through
                string[] hints = state == TestState.Recognized
                    ? [$"Enter {Strings.HintFinish}", $"S {Strings.HintSkip}"]
                    : [$"S {Strings.HintSkip}"];

                return (lines, hints);
            },
            key =>
            {
                while (session.TryRead(out DictationTestEvent testEvent))
                {
                    (state, text, elapsedMs) = testEvent.Kind switch
                    {
                        DictationTestEventKind.RecordingStarted => (TestState.Recording, text, elapsedMs),
                        DictationTestEventKind.Transcribing => (TestState.Transcribing, text, elapsedMs),
                        DictationTestEventKind.Recognized => (TestState.Recognized, testEvent.Text ?? string.Empty, testEvent.ElapsedMs),
                        DictationTestEventKind.NothingHeard => (TestState.NothingHeard, text, elapsedMs),
                        _ => (TestState.Failed, testEvent.Text ?? string.Empty, elapsedMs)
                    };
                }

                bool isDone = SetupUi.IsKey(key, ConsoleKey.S) || (key?.Key == ConsoleKey.Enter && state == TestState.Recognized);

                return isDone ? PollStep<bool>.Finish(UiResult<bool>.Done(true)) : PollStep<bool>.Continue;
            },
            cancellationToken);
    }

    /// <summary>
    ///     Keeps the highlighted microphone open. A device is opened in the background and given up on after
    ///     a while: one that is held by another program may never answer, and the screen must stay alive.
    /// </summary>
    /// <param name="probe"><see cref="IMicrophoneProbe"/></param>
    /// <param name="timeout">How long a device may take to open.</param>
    /// <param name="time">The clock the waiting goes by.</param>
    private sealed class MicrophoneListener(IMicrophoneProbe probe, TimeSpan timeout, TimeProvider time) : IDisposable
    {
        private readonly IMicrophoneProbe _probe = probe;
        private readonly TimeSpan _timeout = timeout;
        private readonly TimeProvider _time = time;

        private long _waitingSince;

        private Task<IMicrophoneSession>? _opening;

        /// <summary>
        ///     The open microphone, once it has answered.
        /// </summary>
        public IMicrophoneSession? Session { get; private set; }

        /// <summary>
        ///     Why the microphone could not be opened, if it could not.
        /// </summary>
        public string? Error { get; private set; }

        public void Switch(int deviceIndex)
        {
            Close();

            Error = null;
            _waitingSince = _time.GetTimestamp();
            _opening = Task.Run(() => _probe.Open(deviceIndex));
        }

        /// <summary>
        ///     Returns the open microphone, or null while it is still opening or has failed.
        /// </summary>
        public IMicrophoneSession? Poll()
        {
            if (Session is not null || _opening is null)
                return Session;

            if (_opening.IsCompletedSuccessfully)
            {
                Session = _opening.Result;
                _opening = null;
            }
            else if (_opening.IsFaulted)
            {
                Error = _opening.Exception!.GetBaseException().Message;
                _opening = null;
            }
            else if (_time.GetElapsedTime(_waitingSince) > _timeout)
            {
                Error = Strings.MicrophoneNoResponse;
                Abandon();
            }

            return Session;
        }

        public void Dispose() => Close();

        private void Close()
        {
            Session?.Dispose();
            Session = null;

            Abandon();
        }

        /// <summary>
        ///     Stops waiting for a device. If it does open later, it is closed right away.
        /// </summary>
        private void Abandon()
        {
            _opening?.ContinueWith(
                opened => opened.Result.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

            _opening = null;
        }
    }
}