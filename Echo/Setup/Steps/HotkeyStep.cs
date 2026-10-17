using Echo.Setup.Hotkeys;
using Echo.UI;
using Spectre.Console;

namespace Echo.Setup.Steps;

/// <summary>
///     Step 4: the push-to-talk key. The user presses the key instead of typing its name. On desktops that give
///     programs their keys themselves (Linux with Wayland), the desktop shows its own window for it instead,
///     and the permission to paste is asked for here as well.
/// </summary>
internal sealed class HotkeyStep : ISetupStep
{
    private readonly SetupUi _ui;
    private readonly IHotkeyCaptureFactory _captureFactory;
    private readonly IDesktopAccess _desktop;

    /// <param name="ui"><see cref="SetupUi"/></param>
    /// <param name="captureFactory"><see cref="IHotkeyCaptureFactory"/></param>
    /// <param name="desktop">What has to be asked of the desktop, if anything.</param>
    public HotkeyStep(SetupUi ui, IHotkeyCaptureFactory captureFactory, IDesktopAccess desktop)
    {
        _ui = ui;
        _captureFactory = captureFactory;
        _desktop = desktop;

        Pages =
        [
            new SetupPage(CaptureAsync, IsNeeded: _ => !_desktop.AssignsKeys),

            new SetupPage(AssignAsync, IsNeeded: _ => _desktop.AssignsKeys),

            // Asked here, in the wizard, and never in the middle of the first dictation
            new SetupPage(
                AskToPasteAsync,
                IsNeeded: _ => _desktop.AsksToPaste && !_desktop.MayPaste,
                SubTitle: () => Strings.SubPastePermission),
        ];
    }

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public string Title => Strings.StepHotkey;

    /// <inheritdoc/>
    public IReadOnlyList<SetupPage> Pages { get; }

    /// <inheritdoc/>
    public IEnumerable<(string Label, string Value)> Summarize(SetupContext context)
    {
        yield return (Strings.SummaryHotkey, HotkeyNames.Display(context.Hotkey, context.HotkeyLabel));
    }

    /// <inheritdoc/>
    public string? Brief(SetupContext context) => HotkeyNames.Display(context.Hotkey, context.HotkeyLabel);

    private async Task<SetupStepResult> CaptureAsync(SetupContext context,
                                                     SetupScreen screen,
                                                     CancellationToken cancellationToken)
    {
        HotkeyChoice original = context.Hotkey;
        HotkeyChoice current = original;

        using IHotkeyCapture capture = _captureFactory.Start();

        string[] hints = [$"Enter {Strings.HintConfirm}"];

        UiResult<HotkeyChoice> result = await _ui.PollAsync<HotkeyChoice>(
            screen,
            () => (
                [
                    SetupUi.Question(Markup.Escape(Strings.HotkeyQuestion)),
                    SetupUi.Dim(Markup.Escape(Strings.HotkeyNote)),
                    string.Empty,
                    $"{SetupUi.Dim(Markup.Escape(Strings.HotkeyCurrent))}  {SetupUi.Accent(Markup.Escape($"[ {HotkeyNames.Display(current)} ]"))}",
                    SetupUi.Small(Markup.Escape(Strings.HotkeyChangeNote)),
                    string.Empty,
                    SetupUi.Warning(Markup.Escape(Strings.HotkeySuppressedNote)),
                ],
                hints),
            key =>
            {
                // The last pressed key wins, so a slip is fixed by simply pressing the right one
                while (capture.TryRead(out HotkeyChoice pressed))
                    current = pressed;

                // The line of steps names the key as well, and follows every press
                context.Hotkey = current;

                return key?.Key == ConsoleKey.Enter
                    ? PollStep<HotkeyChoice>.Finish(UiResult<HotkeyChoice>.Done(current))
                    : PollStep<HotkeyChoice>.Continue;
            },
            cancellationToken);

        if (result.IsBack)
        {
            // A key that was pressed but never confirmed is forgotten
            context.Hotkey = original;

            return SetupStepResult.Back;
        }

        context.Hotkey = result.Value;

        // The key is Echo's own here: a description from a desktop that gave one earlier no longer applies
        context.HotkeyLabel = null;

        return SetupStepResult.Next;
    }

    /// <summary>
    ///     The desktop gives the key: Echo suggests its key, the desktop shows its window, the user confirms
    ///     the key or picks another one there.
    /// </summary>
    private async Task<SetupStepResult> AssignAsync(SetupContext context,
                                                    SetupScreen screen,
                                                    CancellationToken cancellationToken)
    {
        while (true)
        {
            List<string> lines =
            [
                SetupUi.Question(Markup.Escape(Strings.HotkeyDesktopQuestion)),
                SetupUi.Dim(Markup.Escape(Strings.Format(nameof(Strings.HotkeyDesktopNote), HotkeyNames.Display(context.Hotkey)))),
            ];

            if (context.HotkeyLabel is not null)
            {
                lines.AddRange(
                [
                    string.Empty,
                    $"{SetupUi.Dim(Markup.Escape(Strings.HotkeyCurrent))}  {SetupUi.Accent(Markup.Escape($"[ {context.HotkeyLabel} ]"))}",
                ]);
            }

            List<(ConsoleKey Key, string Hint)> keys = [(ConsoleKey.Enter, Strings.HintOpenDesktopWindow)];

            if (context.HotkeyLabel is not null)
                keys.Add((ConsoleKey.Tab, Strings.HintKeep));

            UiResult<ConsoleKey> key = await _ui.ShowAsync(screen, lines, keys, cancellationToken);

            if (key.IsBack)
                return SetupStepResult.Back;

            if (key.Value == ConsoleKey.Tab)
                return SetupStepResult.Next;

            string? label = await _ui.WaitAsync(
                screen,
                Markup.Escape(Strings.HotkeyDesktopWaiting),
                () => AssignOrNullAsync(context.Hotkey, cancellationToken),
                cancellationToken);

            if (label is not null)
            {
                context.HotkeyLabel = label;

                return SetupStepResult.Next;
            }

            UiResult<ConsoleKey> retry = await _ui.ShowAsync(
                screen,
                [_ui.Bad(Markup.Escape(Strings.HotkeyDesktopRefused)), string.Empty, SetupUi.Dim(Markup.Escape(Strings.HotkeyDesktopRefusedNote))],
                [(ConsoleKey.Enter, Strings.HintTryAgain), (ConsoleKey.S, Strings.HintSkip)],
                cancellationToken);

            if (retry.IsBack)
                return SetupStepResult.Back;

            if (retry.Value == ConsoleKey.S)
                return SetupStepResult.Next;
        }
    }

    /// <summary>
    ///     The desktop asks the user whether Echo may press keys. Without it the text is only copied.
    /// </summary>
    private async Task<SetupStepResult> AskToPasteAsync(SetupContext context,
                                                        SetupScreen screen,
                                                        CancellationToken cancellationToken)
    {
        UiResult<ConsoleKey> key = await _ui.ShowAsync(
            screen,
            [
                SetupUi.Question(Markup.Escape(Strings.PastePermissionQuestion)),
                SetupUi.Dim(Markup.Escape(Strings.PastePermissionNote)),
            ],
            [(ConsoleKey.Enter, Strings.HintOpenDesktopWindow), (ConsoleKey.S, Strings.HintSkip)],
            cancellationToken);

        if (key.IsBack)
            return SetupStepResult.Back;

        if (key.Value == ConsoleKey.S)
            return SetupStepResult.Next;

        bool isGranted = await _ui.WaitAsync(
            screen,
            Markup.Escape(Strings.HotkeyDesktopWaiting),
            () => _desktop.AskToPasteAsync(cancellationToken),
            cancellationToken);

        UiResult<ConsoleKey> done = await _ui.ShowAsync(
            screen,
            isGranted
                ? [_ui.Good(Markup.Escape(Strings.PastePermissionGranted))]
                : [_ui.Bad(Markup.Escape(Strings.PastePermissionRefused)), string.Empty, SetupUi.Dim(Markup.Escape(Strings.PastePermissionRefusedNote))],
            [(ConsoleKey.Enter, Strings.HintContinue)],
            cancellationToken);

        return done.IsBack ? SetupStepResult.Back : SetupStepResult.Next;
    }

    /// <summary>
    ///     Asks the desktop for the key; a desktop that has no such window answers like one that gave no key.
    /// </summary>
    private async Task<string?> AssignOrNullAsync(HotkeyChoice preferred, CancellationToken cancellationToken)
    {
        try
        {
            return await _desktop.AssignKeyAsync(preferred, cancellationToken);
        }
        catch (Exception exc) when (exc is not OperationCanceledException)
        {
            return null;
        }
    }
}