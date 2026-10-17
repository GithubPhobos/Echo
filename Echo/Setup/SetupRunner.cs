using Echo.Setup.Hardware;
using Echo.Setup.Steps;
using Echo.UI;
using Spectre.Console;

namespace Echo.Setup;

/// <summary>
///     Shows the pages of the setup steps in order, lets the user move back and forth between them,
///     shows the review screen and saves the result.
/// </summary>
/// <param name="steps">The steps, in the order they were registered.</param>
/// <param name="ui"><see cref="SetupUi"/></param>
/// <param name="settingsStore"><see cref="IUserSettingsStore"/></param>
internal sealed class SetupRunner(IEnumerable<ISetupStep> steps, SetupUi ui, IUserSettingsStore settingsStore)
{
    /// <summary>
    ///     Raised whenever a release adds a step that existing users must go through.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    ///     Stands for "save" in the list of rows on the review screen.
    /// </summary>
    private const int SaveChoice = -1;

    private readonly IReadOnlyList<ISetupStep> _steps = [.. steps];
    private readonly SetupUi _ui = ui;
    private readonly IUserSettingsStore _settingsStore = settingsStore;

    /// <summary>
    ///     The backend chosen in the last completed run, with "automatic" already resolved.
    /// </summary>
    public AccelerationBackend? ChosenBackend { get; private set; }

    /// <summary>
    ///     Whether the wizard has to run before dictation can start.
    /// </summary>
    public bool IsSetupRequired() =>
        _settingsStore.Load().SetupVersion < CurrentVersion;

    /// <summary>
    ///     Runs the wizard.
    /// </summary>
    /// <param name="onlyNewSteps">
    ///     Whether to show only the steps added since the user last completed setup, instead of all of them.
    /// </param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    public async Task<SetupOutcome> RunAsync(bool onlyNewSteps, CancellationToken cancellationToken)
    {
        UserSettings saved = _settingsStore.Load();
        SetupContext context = SetupContext.FromSettings(saved);

        List<ISetupStep> steps = [.. _steps.Where(step => !onlyNewSteps || step.Version > saved.SetupVersion)];
        if (steps.Count == 0)
            return SetupOutcome.NothingToDo;

        List<(int StepIndex, SetupPage Page)> pages =
            [.. steps.SelectMany((step, stepIndex) => step.Pages.Select(page => (stepIndex, page)))];

        // The pages that were really shown, in order: Escape always leads to the one before
        var history = new Stack<int>();
        int current = NextNeeded(pages, context, after: -1);

        while (true)
        {
            while (current < pages.Count)
            {
                SetupStepResult result = await ShowAsync(pages[current], steps, context, history.Count > 0, cancellationToken);

                ApplyLanguage(context);

                if (result == SetupStepResult.Next)
                {
                    history.Push(current);
                    current = NextNeeded(pages, context, after: current);
                }
                else if (history.Count > 0)
                {
                    current = Previous(pages, context, history);
                }
            }

            int? edited = await ReviewAsync(context, steps, cancellationToken);

            if (edited == SaveChoice)
            {
                _settingsStore.Save(context.ToSettings(CurrentVersion));
                ChosenBackend = context.RequestedBackend;

                return SetupOutcome.Completed;
            }

            if (edited is null)
            {
                current = Previous(pages, context, history);

                continue;
            }

            await RunStepAloneAsync(steps, edited.Value, context, cancellationToken);

            ApplyLanguage(context);
        }
    }

    /// <summary>
    ///     Runs the pages of one step, as picked on the review screen. Going back from its first page,
    ///     or forward from its last, returns to the review screen.
    /// </summary>
    private static async Task RunStepAloneAsync(List<ISetupStep> steps,
                                                int stepIndex,
                                                SetupContext context,
                                                CancellationToken cancellationToken)
    {
        List<(int StepIndex, SetupPage Page)> pages = [.. steps[stepIndex].Pages.Select(page => (stepIndex, page))];

        var history = new Stack<int>();
        int current = NextNeeded(pages, context, after: -1);

        while (current < pages.Count)
        {
            SetupStepResult result = await ShowAsync(pages[current], steps, context, canGoBack: true, cancellationToken);

            ApplyLanguage(context);

            if (result == SetupStepResult.Next)
            {
                history.Push(current);
                current = NextNeeded(pages, context, after: current);
            }
            else if (history.Count == 0)
            {
                return;
            }
            else
            {
                current = Previous(pages, context, history);
            }
        }
    }

    private static Task<SetupStepResult> ShowAsync((int StepIndex, SetupPage Page) page,
                                                   List<ISetupStep> steps,
                                                   SetupContext context,
                                                   bool canGoBack,
                                                   CancellationToken cancellationToken)
    {
        // Titles are read again for every page: the first one may have changed the language
        var screen = new SetupScreen(page.StepIndex, Titles(steps), canGoBack, page.Page.SubTitle?.Invoke())
        {
            StepValue = index => steps[index].Brief(context)
        };

        return page.Page.ShowAsync(context, screen, cancellationToken);
    }

    /// <summary>
    ///     Applies the interface language chosen so far. A page that changes the language changes it only
    ///     for itself, since a change of culture never comes back out of an awaited call: the runner
    ///     applies it for the pages that follow.
    /// </summary>
    private static void ApplyLanguage(SetupContext context) =>
        Languages.Apply(context.UiLanguage);

    private static int NextNeeded(List<(int StepIndex, SetupPage Page)> pages, SetupContext context, int after)
    {
        int index = after + 1;

        while (index < pages.Count && pages[index].Page.IsNeeded?.Invoke(context) == false)
            index++;

        return index;
    }

    /// <summary>
    ///     The page shown before the current one. A page that has lost its point in the meantime,
    ///     such as a question about files that have since been downloaded, is passed over.
    /// </summary>
    private static int Previous(List<(int StepIndex, SetupPage Page)> pages, SetupContext context, Stack<int> history)
    {
        int index = history.Pop();

        while (history.Count > 0 && pages[index].Page.IsNeeded?.Invoke(context) == false)
            index = history.Pop();

        return index;
    }

    /// <summary>
    ///     Shows every choice made. Returns the index of the step to run again, <see cref="SaveChoice"/> to save,
    ///     or null to go back.
    /// </summary>
    private async Task<int?> ReviewAsync(SetupContext context, List<ISetupStep> steps, CancellationToken cancellationToken)
    {
        List<Choice<int>> choices =
        [
            .. steps.SelectMany((step, stepIndex) => step.Summarize(context)
                .Select(row => new Choice<int>(stepIndex, row.Label, Summary: Markup.Escape(row.Value)))),
            new(SaveChoice, Strings.ReviewSave, Summary: SetupUi.Recommended(Markup.Escape(Strings.ReviewSaveHint))),
        ];

        UiResult<int> picked = await _ui.SelectAsync(
            new SetupScreen(steps.Count, Titles(steps), CanGoBack: true) { StepValue = index => steps[index].Brief(context) },
            Strings.ReviewQuestion,
            choices,
            selectedIndex: choices.Count - 1,
            [SetupUi.Dim(Markup.Escape(Strings.ReviewNote))],
            cancellationToken);

        return picked.IsBack ? null : picked.Value;
    }

    private static List<string> Titles(List<ISetupStep> steps) =>
        [.. steps.Select(step => step.Title)];
}