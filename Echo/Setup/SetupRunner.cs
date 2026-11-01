namespace Echo.Setup;

/// <summary>
///     Shows the pages of the setup steps in order, lets the user move back and forth between them,
///     shows the review screen and saves the result. A completed setup opens on its review screen instead,
///     so that changing one choice does not take going through every step again.
/// </summary>
/// <param name="steps">The steps, in the order they were registered.</param>
/// <param name="ui"><see cref="SetupUi"/></param>
/// <param name="settingsStore"><see cref="IUserSettingsStore"/></param>
/// <param name="hardwareProbe"><see cref="IHardwareProbe"/></param>
internal sealed class SetupRunner(IEnumerable<ISetupStep> steps,
                                  SetupUi ui,
                                  IUserSettingsStore settingsStore,
                                  IHardwareProbe hardwareProbe)
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
    private readonly IHardwareProbe _hardwareProbe = hardwareProbe;

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

        if (!onlyNewSteps && saved.SetupVersion >= CurrentVersion)
            return await ChangeAsync(context, [.. _steps], cancellationToken);

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
                SetupStepResult result = await ShowAsync(pages[current], steps, context, history.Count > 0, isFromReview: false, cancellationToken);

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

            // Opens on the last row, the one that saves: everything has just been chosen
            int? edited = await ReviewAsync(context, steps, isChange: false, new ListPosition(int.MaxValue), cancellationToken);

            if (edited == SaveChoice)
            {
                Save(context);

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
    ///     Shows the review screen of a completed setup. A row picked there runs the pages of its step alone
    ///     and comes back; Escape leaves without saving anything.
    /// </summary>
    private async Task<SetupOutcome> ChangeAsync(SetupContext context, List<ISetupStep> steps, CancellationToken cancellationToken)
    {
        // Found out first, as the acceleration step does in a full run: the steps after it, the model and
        // the test dictation, count on it, and so does the review row of the automatic acceleration
        context.UseDetected(await _ui.WaitAsync(
            ReviewScreen(steps, context, isChange: true),
            Markup.Escape(Strings.HardwareChecking),
            () => _hardwareProbe.DetectAsync(cancellationToken),
            cancellationToken));

        // Coming back from a step, the row that led to it is still highlighted
        var position = new ListPosition(0);

        while (true)
        {
            int? edited = await ReviewAsync(context, steps, isChange: true, position, cancellationToken);

            if (edited is null)
                return SetupOutcome.Cancelled;

            if (edited == SaveChoice)
            {
                Save(context);

                return SetupOutcome.Completed;
            }

            await RunStepAloneAsync(steps, edited.Value, context, cancellationToken);

            ApplyLanguage(context);
        }
    }

    private void Save(SetupContext context)
    {
        _settingsStore.Save(context.ToSettings(CurrentVersion));
        ChosenBackend = context.RequestedBackend;
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
            SetupStepResult result = await ShowAsync(pages[current], steps, context, canGoBack: true, isFromReview: true, cancellationToken);

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
                                                   bool isFromReview,
                                                   CancellationToken cancellationToken)
    {
        // Titles are read again for every page: the first one may have changed the language
        var screen = new SetupScreen(page.StepIndex, Titles(steps), canGoBack, page.Page.SubTitle?.Invoke())
        {
            StepValue = index => steps[index].Brief(context),
            AreLaterStepsDone = isFromReview
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
    /// <param name="context">The choices.</param>
    /// <param name="steps">The steps the choices belong to.</param>
    /// <param name="isChange">
    ///     Whether a completed setup is being changed: the screen then asks what to change, and Escape leaves it.
    /// </param>
    /// <param name="position">The highlighted row, kept when the screen closes.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    private async Task<int?> ReviewAsync(SetupContext context,
                                         List<ISetupStep> steps,
                                         bool isChange,
                                         ListPosition position,
                                         CancellationToken cancellationToken)
    {
        List<Choice<int>> choices =
        [
            .. steps.SelectMany((step, stepIndex) => step.Summarize(context)
                .Select(row => new Choice<int>(stepIndex, row.Label, Summary: Markup.Escape(row.Value)))),
            isChange
                ? new(SaveChoice, Strings.ReviewChangeSave)
                : new(SaveChoice, Strings.ReviewSave, Summary: SetupUi.Recommended(Markup.Escape(Strings.ReviewSaveHint))),
        ];

        ListKey[] confirm = [new(ConsoleKey.Enter, Strings.HintConfirm)];

        UiResult<(int Value, ConsoleKey Key)> picked = await _ui.SelectAsync(
            ReviewScreen(steps, context, isChange),
            isChange ? Strings.ReviewChangeQuestion : Strings.ReviewQuestion,
            choices,
            position,
            isChange ? null : [SetupUi.Dim(Markup.Escape(Strings.ReviewNote))],
            _ => confirm,
            cancellationToken);

        return picked.IsBack ? null : picked.Value.Value;
    }

    private static SetupScreen ReviewScreen(List<ISetupStep> steps, SetupContext context, bool isChange) =>
        new(steps.Count, Titles(steps), CanGoBack: true)
        {
            StepValue = index => steps[index].Brief(context),
            EscapeHint = isChange ? Strings.HintLeaveUnsaved : null
        };

    private static List<string> Titles(List<ISetupStep> steps) =>
        [.. steps.Select(step => step.Title)];
}