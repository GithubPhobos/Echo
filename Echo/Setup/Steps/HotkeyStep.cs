using Echo.Setup.Hotkeys;
using Echo.UI;
using Spectre.Console;

namespace Echo.Setup.Steps;

/// <summary>
///     Step 4: the push-to-talk key. The user presses the key instead of typing its name.
/// </summary>
internal sealed class HotkeyStep : ISetupStep
{
    private readonly SetupUi _ui;
    private readonly IHotkeyCaptureFactory _captureFactory;

    /// <param name="ui"><see cref="SetupUi"/></param>
    /// <param name="captureFactory"><see cref="IHotkeyCaptureFactory"/></param>
    public HotkeyStep(SetupUi ui, IHotkeyCaptureFactory captureFactory)
    {
        _ui = ui;
        _captureFactory = captureFactory;

        Pages = [new SetupPage(CaptureAsync)];
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
        yield return (Strings.SummaryHotkey, HotkeyNames.Display(context.Hotkey));
    }

    /// <inheritdoc/>
    public string? Brief(SetupContext context) => HotkeyNames.Display(context.Hotkey);

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

        return SetupStepResult.Next;
    }
}