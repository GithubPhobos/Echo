using System.Collections.Concurrent;
using Echo.Setup.Audio;
using Echo.Setup.Check;
using Echo.Setup.Hardware;
using Echo.Setup.Hotkeys;
using Echo.UI;
using Spectre.Console;

namespace Echo.Setup.Steps;

/// <summary>
///     Step 5: sound. The input device is chosen from a list in which every device shows how loud its sound is,
///     so the one that hears the user stands out; the silence level is measured, typed or left as it is on a page
///     of its own, which says what that level is for; then the user dictates one phrase through the real pipeline,
///     with the model, backend and key picked on the previous steps.
/// </summary>
internal sealed class CheckStep : ISetupStep
{
    private const int MeterWidth = 40;
    private const int MeterRows = 3;

    /// <summary>
    ///     How many columns the level of each device takes in the list of devices.
    /// </summary>
    private const int DeviceMeterWidth = 16;

    private const int BriefNameLength = 20;
    private const string BriefCutMark = "...";

    private const string LevelFormat = "0.000";

    /// <summary>
    ///     A short phrase that takes longer than this to recognize means the model is too heavy for the machine.
    /// </summary>
    private const double SlowRecognitionMs = 2500;

    /// <summary>
    ///     What the measuring of the silence level is doing.
    /// </summary>
    private enum Phase
    {
        Opening,
        MeasuringSilence,
        MeasuringVoice,
        VoiceNotHeard
    }

    /// <summary>
    ///     What the user chose to do about the silence level.
    /// </summary>
    private enum Calibrating
    {
        Measure,
        Manual,
        Skip
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

    /// <summary>
    ///     When each device is closed, by its index. A device left on one page may still be opening, and the next page
    ///     opens it again only once that opening is through and closed: two openings of one device at once may find it busy.
    /// </summary>
    private readonly ConcurrentDictionary<int, Task> _closings = new();

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
    ///     The list of input devices. Every one of them is listened to while the list is open and shows next to its
    ///     name how loud its sound is, so the device that hears the user is the one that moves.
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
                [_ui.Bad(Markup.Escape(Strings.MicrophoneNone)), string.Empty, SetupUi.Dim(Markup.Escape(OperatingSystem.IsWindows() ? Strings.MicrophoneNoneHelp : Strings.MicrophoneNoneHelpLinux))],
                [(ConsoleKey.Enter, Strings.HintCheckAgain)],
                cancellationToken);

            if (retry.IsBack)
                return SetupStepResult.Back;
        }

        List<MicrophoneListener> listeners = [.. devices.Select(_ => new MicrophoneListener(_microphoneProbe, _timings.DeviceTimeout, _time, _closings))];
        List<Queue<float>> recent = [.. devices.Select(_ => new Queue<float>(Enumerable.Repeat(0f, DeviceMeterWidth)))];

        for (int index = 0; index < devices.Count; index++)
            listeners[index].Switch(devices[index].Index);

        UiResult<(MicrophoneDevice Value, ConsoleKey Key)> picked;

        try
        {
            picked = await _ui.SelectLiveAsync<MicrophoneDevice>(
                screen,
                Strings.MicrophoneQuestion,
                () => [.. devices.Select((device, index) => new Choice<MicrophoneDevice>(device, device.Name, Summary: DeviceLevel(listeners[index], recent[index])))],
                new ListPosition(Math.Max(0, devices.ToList().FindIndex(device => device.Index == context.MicrophoneIndex))),
                _ => [new ListKey(ConsoleKey.Enter, Strings.HintConfirm)],
                () =>
                {
                    for (int index = 0; index < devices.Count; index++)
                    {
                        if (listeners[index].Poll() is IMicrophoneSession session)
                            Push(recent[index], session.ReadPeak());
                    }
                },
                cancellationToken);
        }
        finally
        {
            // Each device takes a moment to stop, and the next page opens the chosen one again: they are closed together
            await Task.WhenAll(listeners.Select(listener => Task.Run(listener.Dispose)));
        }

        if (picked.IsBack)
            return SetupStepResult.Back;

        MicrophoneDevice chosen = picked.Value.Value;

        // Another device has another silence level: the one measured before no longer applies
        if (context.MicrophoneIndex != chosen.Index || context.MicrophoneName != chosen.Name)
            context.SilenceThreshold = ThresholdCalculator.Default;

        context.MicrophoneIndex = chosen.Index;
        context.MicrophoneName = chosen.Name;

        return SetupStepResult.Next;
    }

    /// <summary>
    ///     How loud a device of the list is right now, on one row: its latest pieces of sound, the newest on the right.
    ///     Dots while it is still opening, a word when it cannot be listened to.
    /// </summary>
    private string DeviceLevel(MicrophoneListener listener, Queue<float> recent)
    {
        if (listener.Error is not null)
            return SetupUi.Dim(Markup.Escape(Strings.MicrophoneUnavailable));

        if (listener.Session is null)
            return SetupUi.Dim(Markup.Escape(string.Concat(Enumerable.Repeat(_ui.Theme.Dot, DeviceMeterWidth))));

        return _ui.Meter([.. recent], rows: 1)[0];
    }

    /// <summary>
    ///     Asks whether to measure the silence level, says what it is for, and does what the user chose:
    ///     measures it, lets them type it, or leaves it as it is.
    /// </summary>
    private async Task<SetupStepResult> MeasureSilenceAsync(SetupContext context,
                                                            SetupScreen screen,
                                                            CancellationToken cancellationToken)
    {
        var position = new ListPosition(0);

        while (true)
        {
            string current = Level(context.SilenceThreshold);

            UiResult<(Calibrating Value, ConsoleKey Key)> picked = await _ui.SelectAsync(
                screen,
                Strings.CalibrationQuestion,
                [
                    new Choice<Calibrating>(Calibrating.Measure, Strings.CalibrationMeasure, Hint: Strings.CalibrationHow),
                    new Choice<Calibrating>(Calibrating.Manual, Strings.CalibrationManual, Hint: Strings.CalibrationManualHint),
                    new Choice<Calibrating>(Calibrating.Skip, Strings.CalibrationSkip, Hint: Strings.Format(nameof(Strings.CalibrationSkipHint), current)),
                ],
                position,
                [SetupUi.Dim(Markup.Escape(Strings.CalibrationWhy)), string.Empty, SetupUi.Dim(SetupUi.Fill(Strings.CalibrationCurrent, current))],
                _ => [new ListKey(ConsoleKey.Enter, Strings.HintConfirm)],
                cancellationToken);

            if (picked.IsBack)
                return SetupStepResult.Back;

            // The same setting as among the additional settings, typed the same way
            bool isDone = picked.Value.Value switch
            {
                Calibrating.Skip => true,
                Calibrating.Manual => await AdditionalSettingsStep.EditAsync(_ui, context, screen, AdditionalSettings.SilenceThreshold, cancellationToken),
                _ => await MeasureAsync(context, screen, cancellationToken)
            };

            // Escape from the typing or the measuring comes back to the question
            if (isDone)
                return SetupStepResult.Next;
        }
    }

    /// <summary>
    ///     Measures the silence level and shows what came out. False when the user went back to the question instead.
    /// </summary>
    private async Task<bool> MeasureAsync(SetupContext context, SetupScreen screen, CancellationToken cancellationToken)
    {
        while (true)
        {
            (UiResult<bool> outcome, Calibration calibration) = await ListenForLevelsAsync(context, screen, cancellationToken);

            if (outcome.IsBack)
                return false;

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
                return true;
        }
    }

    /// <summary>
    ///     Listens first to the room, then to the user's voice, with the seconds left on the screen. It starts as soon
    ///     as the microphone answers.
    /// </summary>
    private async Task<(UiResult<bool> Outcome, Calibration Calibration)> ListenForLevelsAsync(SetupContext context,
                                                                                            SetupScreen screen,
                                                                                            CancellationToken cancellationToken)
    {
        Phase phase = Phase.Opening;
        Calibration calibration = default;

        var quiet = new List<float>();
        var speech = new List<float>();
        var recent = new Queue<float>(Enumerable.Repeat(0f, MeterWidth));
        long measuringSince = 0;

        TimeSpan Measured() => _time.GetElapsedTime(measuringSince);

        using var listener = new MicrophoneListener(_microphoneProbe, _timings.DeviceTimeout, _time, _closings);
        listener.Switch(context.MicrophoneIndex);

        UiResult<bool> outcome = await _ui.PollAsync<bool>(
            screen,
            () =>
            {
                List<string> lines =
                [
                    SetupUi.Question(Markup.Escape(Strings.CalibrationMeasuring)),
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

                // Trying again is offered only when there is something to try again
                string[] hints = phase == Phase.VoiceNotHeard || listener.Error is not null ? [$"Enter {Strings.HintTryAgain}"] : [];

                return (lines, hints);
            },
            key =>
            {
                if (key?.Key == ConsoleKey.Enter && listener.Error is not null)
                {
                    listener.Switch(context.MicrophoneIndex);
                    phase = Phase.Opening;

                    return PollStep<bool>.Continue;
                }

                if (key?.Key == ConsoleKey.Enter && phase == Phase.VoiceNotHeard)
                {
                    Start();

                    return PollStep<bool>.Continue;
                }

                if (key is not null || listener.Poll() is not IMicrophoneSession session)
                    return PollStep<bool>.Continue;

                // The measurement starts as soon as the microphone answers
                if (phase == Phase.Opening)
                    Start();

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
                            return PollStep<bool>.Finish(UiResult<bool>.Done(true));

                        phase = Phase.VoiceNotHeard;
                    }
                }

                return PollStep<bool>.Continue;
            },
            cancellationToken);

        return (outcome, calibration);

        void Start()
        {
            phase = Phase.MeasuringSilence;
            quiet.Clear();
            speech.Clear();
            measuringSince = _time.GetTimestamp();
        }
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
                    () => StartDictationAsync(context, cancellationToken),
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

    /// <summary>
    ///     Starts the test dictation once the device is closed on the pages before, or once it has had the time a device
    ///     may take to open: the dictation opens the device on its own, and two openings at once may find it busy.
    /// </summary>
    private async Task<IDictationTestSession> StartDictationAsync(SetupContext context, CancellationToken cancellationToken)
    {
        if (_closings.TryGetValue(context.MicrophoneIndex, out Task? closing))
            await closing.WaitAsync(_timings.DeviceTimeout, _time, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        return await _dictationTester.StartAsync(context, cancellationToken);
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

        string question = Strings.Format(nameof(Strings.TestQuestion), HotkeyNames.Display(context.Hotkey, context.HotkeyLabel));

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
    ///     Keeps a microphone open while a page listens to it. A device is opened in the background and given up on after
    ///     a while: one that is held by another program may never answer, and the screen must stay alive.
    /// </summary>
    /// <param name="probe"><see cref="IMicrophoneProbe"/></param>
    /// <param name="timeout">How long a device may take to open.</param>
    /// <param name="time">The clock the waiting goes by.</param>
    /// <param name="closings">When each device is closed, shared by the pages of the step.</param>
    private sealed class MicrophoneListener(IMicrophoneProbe probe,
                                            TimeSpan timeout,
                                            TimeProvider time,
                                            ConcurrentDictionary<int, Task> closings) : IDisposable
    {
        private readonly IMicrophoneProbe _probe = probe;
        private readonly TimeSpan _timeout = timeout;
        private readonly TimeProvider _time = time;
        private readonly ConcurrentDictionary<int, Task> _closings = closings;

        private int _deviceIndex;
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
            _deviceIndex = deviceIndex;
            _waitingSince = _time.GetTimestamp();

            // The device opens once an opening of it left on the page before is closed; the waiting counts towards the
            // time it may take
            Task closed = _closings.TryGetValue(deviceIndex, out Task? closing) ? closing : Task.CompletedTask;
            _opening = closed.ContinueWith(_ => _probe.Open(deviceIndex), TaskScheduler.Default);
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

                // A device that does not answer is not waited for any more: trying again opens it anew
                _closings.TryRemove(_deviceIndex, out _);
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
        ///     Stops waiting for a device. If it does open later, it is closed right away, and counts as closed only then.
        /// </summary>
        private void Abandon()
        {
            if (_opening is null)
                return;

            _closings[_deviceIndex] = _opening.ContinueWith(
                opened =>
                {
                    if (opened.IsCompletedSuccessfully)
                        opened.Result.Dispose();
                },
                TaskScheduler.Default);

            _opening = null;
        }
    }
}