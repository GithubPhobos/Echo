using Echo.Setup.Downloads;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Echo.UI;

/// <summary>
///     Draws the setup wizard screens and reads the keyboard. Every screen replaces the previous one in place.
/// </summary>
/// <param name="console">The console to draw on.</param>
/// <param name="theme">The symbols and colors to draw with.</param>
/// <param name="tick">How often polled screens refresh.</param>
internal sealed class SetupUi(IAnsiConsole console, Theme theme, TimeSpan tick)
{
    private const int PanelWidth = 96;
    private const int MinBodyRows = 9;
    private const int ProgressBarWidth = 40;

    private const string StepIndent = "  ";

    /// <summary>
    ///     Stands for the rows of a list that are out of sight.
    /// </summary>
    private const string More = "...";

    /// <summary>
    ///     A list that cannot show this many of its rows gives up what it can do without to make room.
    /// </summary>
    private const int MinRowsInSight = 3;

    private readonly IAnsiConsole _console = console;
    private readonly TimeSpan _tick = tick;

    public Theme Theme { get; } = theme;

    /// <summary>
    ///     Lets the user pick one of <paramref name="choices"/> with the arrow keys.
    /// </summary>
    /// <param name="screen">Where the screen is in the wizard.</param>
    /// <param name="question">The question above the list.</param>
    /// <param name="choices">The rows of the list.</param>
    /// <param name="selectedIndex">The row highlighted at first.</param>
    /// <param name="notes">Markup lines shown between the question and the list.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    public async Task<UiResult<T>> SelectAsync<T>(SetupScreen screen,
                                                  string question,
                                                  IReadOnlyList<Choice<T>> choices,
                                                  int selectedIndex,
                                                  IReadOnlyList<string>? notes,
                                                  CancellationToken cancellationToken)
    {
        ListKey[] confirm = [new(ConsoleKey.Enter, Strings.HintConfirm)];

        UiResult<(T Value, ConsoleKey Key)> picked = await SelectAsync(
            screen,
            question,
            choices,
            new ListPosition(selectedIndex),
            notes,
            _ => confirm,
            cancellationToken);

        return picked.IsBack ? UiResult<T>.Back : UiResult<T>.Done(picked.Value.Value);
    }

    /// <summary>
    ///     Lets the user pick one of <paramref name="choices"/> with any of the keys the highlighted row accepts.
    /// </summary>
    /// <param name="screen">Where the screen is in the wizard.</param>
    /// <param name="question">The question above the list.</param>
    /// <param name="choices">The rows of the list.</param>
    /// <param name="position">Where the list opens, and where it was when it closed.</param>
    /// <param name="notes">Markup lines shown between the question and the list.</param>
    /// <param name="keys">The keys that pick the given row. Asked for the highlighted row only.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    /// <returns>The picked row and the key it was picked with.</returns>
    public async Task<UiResult<(T Value, ConsoleKey Key)>> SelectAsync<T>(SetupScreen screen,
                                                                          string question,
                                                                          IReadOnlyList<Choice<T>> choices,
                                                                          ListPosition position,
                                                                          IReadOnlyList<string>? notes,
                                                                          Func<T, IReadOnlyList<ListKey>> keys,
                                                                          CancellationToken cancellationToken)
    {
        position.Current = Math.Clamp(position.Current, 0, choices.Count - 1);

        return await RunAsync<(T Value, ConsoleKey Key)>(
            screen,
            () =>
            {
                string[] hints =
                [
                    $"{Theme.Arrows} {Strings.HintSelect}",
                    .. keys(choices[position.Current].Value)
                        .Where(key => key.Hint is not null)
                        .Select(key => $"{KeyName(key.Key)} {key.Hint}"),
                ];

                return (BuildSelection(screen, question, choices, position, notes, hints), hints);
            },
            isPolled: false,
            key =>
            {
                switch (key?.Key)
                {
                    case ConsoleKey.UpArrow:
                        position.Current = (position.Current + choices.Count - 1) % choices.Count;

                        return PollStep<(T Value, ConsoleKey Key)>.Continue;

                    case ConsoleKey.DownArrow:
                        position.Current = (position.Current + 1) % choices.Count;

                        return PollStep<(T Value, ConsoleKey Key)>.Continue;
                }

                T value = choices[position.Current].Value;

                foreach (ListKey accepted in keys(value))
                {
                    if (IsKey(key, accepted.Key))
                        return PollStep<(T Value, ConsoleKey Key)>.Finish(UiResult<(T Value, ConsoleKey Key)>.Done((value, accepted.Key)));
                }

                return PollStep<(T Value, ConsoleKey Key)>.Continue;
            },
            cancellationToken);
    }

    /// <summary>
    ///     Shows text and waits for one of the listed keys.
    /// </summary>
    /// <param name="screen">Where the screen is in the wizard.</param>
    /// <param name="lines">Markup lines to show.</param>
    /// <param name="keys">The accepted keys and what each of them does, for the hint line.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    public Task<UiResult<ConsoleKey>> ShowAsync(SetupScreen screen,
                                                IReadOnlyList<string> lines,
                                                IReadOnlyList<(ConsoleKey Key, string Hint)> keys,
                                                CancellationToken cancellationToken)
    {
        string[] hints = [.. keys.Select(key => $"{KeyName(key.Key)} {key.Hint}")];

        return RunAsync<ConsoleKey>(
            screen,
            () => (ToRenderables(lines), hints),
            isPolled: false,
            key =>
            {
                (ConsoleKey Key, string Hint) pressed = keys.FirstOrDefault(accepted => IsKey(key, accepted.Key));

                return pressed.Hint is null
                    ? PollStep<ConsoleKey>.Continue
                    : PollStep<ConsoleKey>.Finish(UiResult<ConsoleKey>.Done(pressed.Key));
            },
            cancellationToken);
    }

    /// <summary>
    ///     Shows a screen that changes on its own: it is redrawn on every tick, not only on a key press.
    /// </summary>
    /// <param name="screen">Where the screen is in the wizard.</param>
    /// <param name="render">Builds the markup lines and the key hints for the current state.</param>
    /// <param name="onTick">Called with every pressed key, and with null once per tick.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    public Task<UiResult<T>> PollAsync<T>(SetupScreen screen,
                                          Func<(IReadOnlyList<string> Lines, IReadOnlyList<string> Hints)> render,
                                          Func<ConsoleKeyInfo?, PollStep<T>> onTick,
                                          CancellationToken cancellationToken,
                                          string? escapeHint = null)
    {
        return RunAsync(
            screen,
            () =>
            {
                (IReadOnlyList<string> lines, IReadOnlyList<string> hints) = render();

                return (ToRenderables(lines), hints);
            },
            isPolled: true,
            onTick,
            cancellationToken,
            escapeHint);
    }

    /// <summary>
    ///     Runs a download with a progress bar. Escape stops the work and leaves the screen open,
    ///     so it can be continued from there; only a second Escape leaves the screen.
    /// </summary>
    /// <param name="screen">Where the screen is in the wizard.</param>
    /// <param name="header">Builds the markup lines above the bar; asked again on every refresh.</param>
    /// <param name="work">The work itself. It is started again when the user continues after stopping it.</param>
    /// <param name="stoppedMessage">What to say while the work is stopped.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    /// <returns>Back when the user left; otherwise null on success or the error that stopped the work.</returns>
    public Task<UiResult<Exception?>> ProgressAsync(SetupScreen screen,
                                                    Func<IReadOnlyList<string>> header,
                                                    Func<IProgress<DownloadProgress>, CancellationToken, Task> work,
                                                    string stoppedMessage,
                                                    CancellationToken cancellationToken) =>
        RunAsync(screen, latest => [.. header(), string.Empty, .. BuildProgress(latest)], work, stoppedMessage, cancellationToken);

    /// <summary>
    ///     Copies a file the user already has, shown as steps rather than a progress bar: the file was found on the
    ///     disk, it is being copied into Echo's folder, done. Nothing comes from the internet, so nothing looks like
    ///     a download. Escape stops and continues the copy as it does a download.
    /// </summary>
    /// <param name="screen">Where the screen is in the wizard.</param>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="sizeBytes">The size of the file.</param>
    /// <param name="work">The copy itself. It is started again when the user continues after stopping it.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    /// <returns>Back when the user left; otherwise null on success or the error that stopped the copy.</returns>
    public Task<UiResult<Exception?>> CopyAsync(SetupScreen screen,
                                                string fileName,
                                                long sizeBytes,
                                                Func<IProgress<DownloadProgress>, CancellationToken, Task> work,
                                                CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();

        return RunAsync(screen, latest => BuildCopy(fileName, sizeBytes, latest, clock.Elapsed), work, Strings.CopyPaused, cancellationToken);
    }

    /// <summary>
    ///     Runs work that reports its progress. Escape stops the work and leaves the screen open, so it can be
    ///     continued from there; only a second Escape leaves the screen.
    /// </summary>
    /// <param name="screen">Where the screen is in the wizard.</param>
    /// <param name="lines">Builds the markup lines of the screen from the latest progress; asked again on every refresh.</param>
    /// <param name="work">The work itself. It is started again when the user continues after stopping it.</param>
    /// <param name="stoppedMessage">What to say while the work is stopped.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    private async Task<UiResult<Exception?>> RunAsync(SetupScreen screen,
                                                      Func<DownloadProgress, IReadOnlyList<string>> lines,
                                                      Func<IProgress<DownloadProgress>, CancellationToken, Task> work,
                                                      string stoppedMessage,
                                                      CancellationToken cancellationToken)
    {
        DownloadProgress latest = default;
        var progress = new InlineProgress<DownloadProgress>(update => latest = update);

        while (true)
        {
            using var workCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            Task task = Task.Run(() => work(progress, workCancellation.Token), workCancellation.Token);

            UiResult<Exception?> result = await PollAsync<Exception?>(
                screen,
                () => (lines(latest), []),
                _ => task.IsCompleted
                    ? PollStep<Exception?>.Finish(UiResult<Exception?>.Done(task.Exception?.GetBaseException()))
                    : PollStep<Exception?>.Continue,
                cancellationToken,
                escapeHint: Strings.HintCancel);

            if (!result.IsBack)
                return result;

            workCancellation.Cancel();

            // The partial file must be closed before the work can be continued
            await task.ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

            // Escape came just as the work finished: there is nothing left to stop
            if (task.IsCompletedSuccessfully)
                return UiResult<Exception?>.Done(null);

            UiResult<ConsoleKey> stopped = await ShowAsync(
                screen,
                [.. lines(latest), string.Empty, Warning(Markup.Escape(stoppedMessage))],
                [(ConsoleKey.Enter, Strings.HintContinue)],
                cancellationToken);

            if (stopped.IsBack)
                return UiResult<Exception?>.Back;
        }
    }

    /// <summary>
    ///     Shows a waiting message until <paramref name="work"/> finishes.
    /// </summary>
    public async Task<T> WaitAsync<T>(SetupScreen screen, string message, Func<Task<T>> work, CancellationToken cancellationToken)
    {
        Task<T> task = Task.Run(work, cancellationToken);
        int ticks = 0;

        await PollAsync<bool>(
            screen with { CanGoBack = false },
            () => ([$"{message} {Dim(new string('.', ticks++ / 4 % 4))}"], []),
            _ => task.IsCompleted ? PollStep<bool>.Finish(UiResult<bool>.Done(true)) : PollStep<bool>.Continue,
            cancellationToken);

        return await task;
    }

    /// <summary>
    ///     A meter several rows tall for levels between 0 and 1: one column of bars per level,
    ///     the latest on the right. Returns its rows from the top one down.
    /// </summary>
    public IReadOnlyList<string> Meter(IReadOnlyList<float> levels, int rows)
    {
        int steps = Theme.LevelBars.Length;
        var lines = new List<string>();

        for (int row = rows - 1; row >= 0; row--)
        {
            var sb = new StringBuilder();

            foreach (float level in levels)
            {
                // Speech rarely exceeds a third of the full scale, so the meter is stretched towards quiet sounds
                int height = (int)Math.Round(Math.Sqrt(Math.Clamp(level * 3f, 0f, 1f)) * (rows * steps - 1));
                int reach = height - row * steps;

                sb.Append(reach >= steps ? Theme.LevelBars[^1] : reach >= 0 ? Theme.LevelBars[reach] : ' ');
            }

            // The louder part of the meter is drawn in the brighter color
            lines.Add($"[{(row == 0 ? Theme.Accent : Theme.Highlight)}]{sb}[/]");
        }

        return lines;
    }

    /// <summary>
    ///     Lets the user type a line of text.
    /// </summary>
    /// <param name="screen">Where the screen is in the wizard.</param>
    /// <param name="question">What is being asked for.</param>
    /// <param name="notes">Markup lines shown between the question and the field.</param>
    /// <param name="initial">The text the field starts with.</param>
    /// <param name="check">Returns what is wrong with the typed text, or null when it can be accepted.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    public Task<UiResult<string>> InputAsync(SetupScreen screen,
                                             string question,
                                             IReadOnlyList<string> notes,
                                             string initial,
                                             Func<string, string?> check,
                                             CancellationToken cancellationToken)
    {
        var typed = new StringBuilder(initial);
        string? problem = null;

        string[] hints = [$"Enter {Strings.HintConfirm}"];

        return RunAsync<string>(
            screen,
            () =>
            {
                List<string> lines =
                [
                    Question(Markup.Escape(question)),
                    .. notes,
                    string.Empty,
                    $"[bold {Theme.Highlight}]{Theme.Pointer}[/] {Markup.Escape(typed.ToString())}[{Theme.Accent}]{Theme.BarFull}[/]",
                    string.Empty,
                    problem is null ? string.Empty : Bad(Markup.Escape(problem)),
                ];

                return (ToRenderables(lines), hints);
            },
            isPolled: false,
            key =>
            {
                if (key is not ConsoleKeyInfo pressed)
                    return PollStep<string>.Continue;

                if (pressed.Key == ConsoleKey.Enter)
                {
                    problem = check(typed.ToString());

                    return problem is null
                        ? PollStep<string>.Finish(UiResult<string>.Done(typed.ToString()))
                        : PollStep<string>.Continue;
                }

                if (pressed.Key == ConsoleKey.Backspace)
                {
                    if (typed.Length > 0)
                        typed.Length--;
                }
                else if (!char.IsControl(pressed.KeyChar))
                {
                    typed.Append(pressed.KeyChar);
                }

                problem = null;

                return PollStep<string>.Continue;
            },
            cancellationToken,
            escapeHint: Strings.HintCancel);
    }

    /// <summary>
    ///     Whether <paramref name="key"/> is the given key. Letters are also recognized by the typed character:
    ///     some terminals deliver them without a key code, depending on the keyboard layout.
    /// </summary>
    /// <summary>
    ///     Gives the terminal to a program for a while: the screen is cleared, the heading says what is going on,
    ///     and once the program is done the wizard draws its screens again from a clean terminal.
    /// </summary>
    /// <param name="heading">What is about to happen, as markup.</param>
    /// <param name="run">Runs the program.</param>
    public async Task<T> HandOverAsync<T>(string heading, Func<Task<T>> run)
    {
        _console.Clear();
        _console.MarkupLine(heading);
        _console.WriteLine();
        _console.Cursor.Show(true);

        try
        {
            return await run();
        }
        finally
        {
            _console.Clear();
        }
    }

    public static bool IsKey(ConsoleKeyInfo? key, ConsoleKey expected)
    {
        if (key is not ConsoleKeyInfo pressed)
            return false;

        if (pressed.Key == expected)
            return true;

        if (expected == ConsoleKey.Spacebar)
            return pressed.KeyChar == ' ';

        return expected is >= ConsoleKey.A and <= ConsoleKey.Z
            && char.ToUpperInvariant(pressed.KeyChar) == (char)expected;
    }

    public static string Dim(string text) => $"[{Theme.Dim}]{text}[/]";

    /// <summary>
    ///     A remark that matters less than the text around it. A terminal has one font size, so it is
    ///     set apart by being faint and slanted instead of smaller.
    /// </summary>
    public static string Small(string text) => $"[italic {Theme.Dim}]{text}[/]";

    /// <summary>
    ///     Something the user should not overlook.
    /// </summary>
    public static string Warning(string text) => $"[{Theme.Warning}]{text}[/]";

    /// <summary>
    ///     A number worth noticing inside other text, such as the size of a download.
    /// </summary>
    public static string Emphasis(string text) => $"[bold {Theme.Accent}]{text}[/]";

    /// <summary>
    ///     Fills the placeholders of a plain text with values that stand out from it.
    ///     Neither the text nor the values are markup; the result is.
    /// </summary>
    public static string Fill(string text, params string[] values) =>
        string.Format(
            System.Globalization.CultureInfo.CurrentUICulture,
            Markup.Escape(text),
            [.. values.Select(value => Accent(Markup.Escape(value)))]);

    public static string Recommended(string text) => $"[{Theme.Highlight}]{text}[/]";

    public static string Done(string text) => $"[{Theme.Success}]{text}[/]";

    public static string Accent(string text) => $"[bold {Theme.Accent}]{text}[/]";

    public static string Question(string text) => $"[bold]{text}[/]";

    public string Good(string text) => $"[bold {Theme.Success}]{Theme.Good}[/] {text}";

    public string Bad(string text) => $"[bold {Theme.Failure}]{Theme.Bad}[/] [{Theme.Failure}]{text}[/]";

    private async Task<UiResult<T>> RunAsync<T>(SetupScreen screen,
                                                Func<(IReadOnlyList<IRenderable> Body, IReadOnlyList<string> Hints)> render,
                                                bool isPolled,
                                                Func<ConsoleKeyInfo?, PollStep<T>> onKey,
                                                CancellationToken cancellationToken,
                                                string? escapeHint = null)
    {
        UiResult<T> result = default;

        await _console.Live(Compose(screen, render, escapeHint))
            .AutoClear(true)
            .StartAsync(async context =>
            {
                while (true)
                {
                    context.UpdateTarget(Compose(screen, render, escapeHint));
                    context.Refresh();

                    if (!isPolled)
                    {
                        ConsoleKeyInfo? key = await _console.Input.ReadKeyAsync(intercept: true, cancellationToken);

                        if (Handle(key, out result))
                            return;

                        // A held key repeats faster than a screen is drawn. Everything that has piled up is
                        // taken before the next drawing, or the list would go on moving long after the key is released
                        while (_console.Input.IsKeyAvailable())
                        {
                            if (Handle(await _console.Input.ReadKeyAsync(intercept: true, cancellationToken), out result))
                                return;
                        }

                        continue;
                    }

                    while (_console.Input.IsKeyAvailable())
                    {
                        if (Handle(await _console.Input.ReadKeyAsync(intercept: true, cancellationToken), out result))
                            return;
                    }

                    if (Handle(null, out result))
                        return;

                    await Task.Delay(_tick, cancellationToken);
                }
            });

        return result;

        bool Handle(ConsoleKeyInfo? key, out UiResult<T> outcome)
        {
            if (key?.Key == ConsoleKey.Escape && screen.CanGoBack)
            {
                outcome = UiResult<T>.Back;

                return true;
            }

            PollStep<T> step = onKey(key);
            outcome = step.Result;

            return step.IsFinished;
        }
    }

    /// <summary>
    ///     Puts a screen together. The box is kept at the same height from screen to screen, so the interface
    ///     does not jump, but the spare rows are given up one by one when the terminal is too short for them,
    ///     and after them the empty rows that only give the screen some air.
    /// </summary>
    private Rows Compose(SetupScreen screen,
                         Func<(IReadOnlyList<IRenderable> Body, IReadOnlyList<string> Hints)> render,
                         string? escapeHint)
    {
        (IReadOnlyList<IRenderable> body, IReadOnlyList<string> hints) = render();

        int bodyRows = RowsOf(body);

        for (int minBodyRows = MinBodyRows; minBodyRows >= 0; minBodyRows--)
        {
            Rows composed = Build(screen, body, hints, escapeHint, minBodyRows, isCompact: false);

            if (HeightOf(composed) <= MaxHeight)
                return composed;

            // A body that already has this many rows gets no spare ones: there is nothing more to give up
            if (minBodyRows <= bodyRows)
                break;
        }

        Rows compact = Build(screen, body, hints, escapeHint, minBodyRows: 0, isCompact: true);

        if (HeightOf(compact) <= MaxHeight)
            return compact;

        // Still too tall: the terminal would cut the bottom off, and the keys with it. The explanation under the
        // question gives way instead, line by line, and "..." says that something is left out
        List<IRenderable> shortened = [.. body];
        shortened.Insert(Math.Min(1, shortened.Count), new Markup(Dim(More)));

        while (shortened.Count > 3)
        {
            shortened.RemoveAt(2);
            compact = Build(screen, shortened, hints, escapeHint, minBodyRows: 0, isCompact: true);

            if (HeightOf(compact) <= MaxHeight)
                break;
        }

        return compact;
    }

    private static int RowsOf(IReadOnlyList<IRenderable> body) =>
        body.Sum(item => item is Grid grid ? grid.Rows.Count : 1);

    private Rows Build(SetupScreen screen,
                       IReadOnlyList<IRenderable> body,
                       IReadOnlyList<string> hints,
                       string? escapeHint,
                       int minBodyRows,
                       bool isCompact)
    {
        List<IRenderable> content = isCompact ? [.. body] : [new Markup(" "), .. body];

        for (int rows = RowsOf(body); rows < minBodyRows; rows++)
            content.Add(new Markup(" "));

        if (!isCompact)
            content.Add(new Markup(" "));

        var panel = new Panel(new Rows(content))
        {
            Border = BoxBorder.Double,
            BorderStyle = Style.Parse(Theme.Dim),
            Header = new PanelHeader($"[bold {Theme.Highlight}] {Markup.Escape(Header(screen))} [/]"),
            Padding = new Padding(2, 0, 2, 0),
            Width = BoxWidth
        };

        List<string> allHints = [.. hints];
        if (screen.CanGoBack)
            allHints.Add($"Esc {escapeHint ?? Strings.HintBack}");

        IRenderable[] air = isCompact ? [] : [new Markup(" ")];

        return new Rows(
        [
            .. air,
            .. StepLines(screen).Select(line => new Markup(StepIndent + line)),
            new Markup(" "),
            new Padder(panel, new Padding(2, 0, 0, 0)),
            new Markup(" "),
            new Markup("  " + Dim(Markup.Escape(string.Join($"  {Theme.Dot}  ", allHints)))),
            .. air,
        ]);
    }

    /// <summary>
    ///     The line of steps: always all of them, each with what was chosen in it so far. A line too long
    ///     for the terminal continues on the next row, and is only ever broken between two steps.
    /// </summary>
    private List<string> StepLines(SetupScreen screen)
    {
        string separator = $"  {Dim(Theme.StepSeparator)}";
        int room = _console.Profile.Width - StepIndent.Length - 1;

        var lines = new List<string>();
        string line = string.Empty;

        for (int index = 0; index < screen.StepTitles.Count; index++)
        {
            string step = Step(screen, index);

            if (line.Length == 0)
                line = step;
            else if (WidthOf($"{line}{separator}  {step}") + (index < screen.StepTitles.Count - 1 ? WidthOf(separator) : 0) <= room)
                line = $"{line}{separator}  {step}";
            else
            {
                lines.Add(line + separator);
                line = step;
            }
        }

        lines.Add(line);

        return lines;
    }

    private string Step(SetupScreen screen, int index)
    {
        string text = Markup.Escape($"{index + 1} {screen.StepTitles[index]}");

        // What lies ahead has no choice to show yet, whatever it starts out with
        string? chosen = index <= screen.StepIndex ? screen.StepValue?.Invoke(index) : null;
        string value = string.IsNullOrEmpty(chosen) ? string.Empty : Markup.Escape($" ({chosen})");

        if (index < screen.StepIndex)
            return $"[{Theme.Accent}]{Theme.Good} {text}{value}[/]";

        return index > screen.StepIndex
            ? Dim(text)
            : $"[bold {Theme.Highlight}]{text}[/][{Theme.Highlight}]{value}[/]";
    }

    private string Header(SetupScreen screen)
    {
        if (screen.IsReview)
            return $"{Strings.SetupTitle} {Theme.Dot} {Strings.SetupReview}";

        string position = Strings.Format(nameof(Strings.SetupStepOf), screen.StepIndex + 1, screen.StepTitles.Count);
        string header = $"{Strings.SetupTitle} {Theme.Dot} {position} {Theme.Dot} {screen.StepTitles[screen.StepIndex]}";

        return screen.SubTitle is null ? header : $"{header} {Theme.Dot} {screen.SubTitle}";
    }

    /// <summary>
    ///     The most rows a screen may take: one less than the terminal has, so nothing scrolls away.
    /// </summary>
    private int MaxHeight => _console.Profile.Height - 1;

    /// <summary>
    ///     The width of the box: as wide as it reads well, but never wider than the terminal.
    /// </summary>
    private int BoxWidth => Math.Min(PanelWidth, Math.Max(40, _console.Profile.Width - 4));

    /// <summary>
    ///     The room for text inside the box, between its frame and its padding.
    /// </summary>
    private int InnerWidth => BoxWidth - 6;

    private int HeightOf(IRenderable renderable) =>
        HeightOf(renderable, _console.Profile.Width);

    /// <summary>
    ///     How many cells of a row the markup takes when nothing makes it wrap.
    /// </summary>
    private int WidthOf(string markup) =>
        ((IRenderable)new Markup(markup)).Measure(RenderOptions.Create(_console, _console.Profile.Capabilities), int.MaxValue).Max;

    private int HeightOf(IRenderable renderable, int width) =>
        Segment
            .SplitLines(renderable.Render(RenderOptions.Create(_console, _console.Profile.Capabilities), width))
            .Count;

    /// <summary>
    ///     What one line of a list is.
    /// </summary>
    private enum ListLine
    {
        /// <summary>A choice.</summary>
        Row,

        /// <summary>The name of a block of choices.</summary>
        Heading,

        /// <summary>An empty line.</summary>
        Gap,

        /// <summary>Stands for the choices out of sight.</summary>
        More
    }

    /// <summary>
    ///     How much a list shows besides its rows, from the least to the most.
    /// </summary>
    private enum ListDetail
    {
        /// <summary>Nothing but the rows.</summary>
        Rows,

        /// <summary>The marks of rows out of sight as well.</summary>
        Marks,

        /// <summary>The names of the blocks too, with an empty line on either side.</summary>
        Full
    }

    private List<IRenderable> BuildSelection<T>(SetupScreen screen,
                                                string question,
                                                IReadOnlyList<Choice<T>> choices,
                                                ListPosition position,
                                                IReadOnlyList<string>? notes,
                                                IReadOnlyList<string> hints)
    {
        var body = new List<IRenderable> { new Markup(Question(Markup.Escape(question))) };

        // An empty note is an empty line
        foreach (string note in notes ?? [])
            body.Add(new Markup(note.Length == 0 ? " " : note));

        body.Add(new Markup(" "));

        // The rows set apart at the end stay in sight whatever the list above them is scrolled to
        int listed = choices.TakeWhile(choice => !choice.IsSetApart).Count();
        int pinnedRows = (choices.Count - listed) * 2;

        List<IRenderable> explanation = BuildExplanation(choices, position.Current, isSteady: true);
        ListDetail detail = ListDetail.Full;

        (int first, int last, int capacity) = Fit();

        // A terminal with few rows: first the list gives up the names of its blocks, then the box stops
        // keeping its height from row to row, and at last the marks of rows out of sight go too.
        // What is given up does not depend on the highlighted row, so the list does not change its look
        // while the user moves through it
        if (IsCramped())
        {
            detail = ListDetail.Marks;

            (first, last, capacity) = Fit();
        }

        if (IsCramped())
        {
            explanation = BuildExplanation(choices, position.Current, isSteady: false);

            (first, last, capacity) = Fit();
        }

        if (IsCramped())
        {
            detail = ListDetail.Rows;

            (first, last, capacity) = Fit();
        }

        bool hasRemarks = choices.Any(choice => choice.Remark is not null);

        // The columns are as wide as the widest row of the whole list, shown or not: the list keeps its
        // shape while it scrolls, and its blocks line up with each other. A row set apart is left out of
        // that: however long its label is, the summaries of the list stay next to their labels
        List<Choice<T>> aligned = [.. choices.Where(choice => !choice.IsSetApart)];

        int labelWidth = aligned.Count == 0 ? 0 : aligned.Max(choice => WidthOf(Markup.Escape(choice.Label)));
        int summaryWidth = aligned.Count == 0 ? 0 : aligned.Max(choice => WidthOf(choice.Summary ?? string.Empty));

        Grid? grid = null;

        List<(ListLine Kind, int Index)> lines = Layout(choices, first, last, listed, detail);

        foreach ((ListLine kind, int index) in lines)
        {
            switch (kind)
            {
                case ListLine.Heading:
                    Close();
                    body.Add(new Markup($"[bold]{Markup.Escape(choices[index].Group!)}[/]"));

                    break;

                case ListLine.Gap:
                    Close();
                    body.Add(new Markup(" "));

                    break;

                case ListLine.More:

                    // Indented like a row even when it is the only line of its part of the list
                    AddRow(
                        isSetApart: false,
                        new Markup(new string(' ', Theme.Pointer.Length + 1 + Theme.RadioOff.Length)),
                        new Markup(Dim(More)),
                        Text.Empty,
                        Text.Empty);

                    break;

                default:
                    AddChoice(index);

                    break;
            }
        }

        Close();

        // A list that scrolls takes all the lines it has, whatever falls into sight: the rows under it stay put
        if (first > 0 || last < listed - 1)
        {
            for (int line = lines.Count; line < capacity; line++)
                body.Add(new Markup(" "));
        }

        for (int index = listed; index < choices.Count; index++)
        {
            Close();

            if (index > 0)
                body.Add(new Markup(" "));

            AddChoice(index);
        }

        Close();

        body.AddRange(explanation);

        return body;

        // The list gets the rows the terminal has left: a screen taller than the terminal would lose its top,
        // and with it the navigation. Every line of a list is one row tall, so one measurement tells it
        (int First, int Last, int Capacity) Fit()
        {
            int taken = HeightOf(Build(screen, [.. body, .. explanation], hints, escapeHint: null, minBodyRows: 0, isCompact: true));
            int room = MaxHeight - taken - pinnedRows;

            (int from, int to) = Window(choices, listed, position, room, detail);

            return (from, to, room);
        }

        bool IsCramped() =>
            last - first + 1 < Math.Min(MinRowsInSight, listed);

        void AddChoice(int index)
        {
            Choice<T> choice = choices[index];
            bool isCurrent = index == position.Current;

            string label = Markup.Escape(choice.Label);
            string summary = choice.Summary ?? string.Empty;
            string remark = choice.Remark ?? string.Empty;

            // Colors set inside a summary survive the dimming of the rows that are not highlighted
            AddRow(
                choice.IsSetApart,
                new Markup(isCurrent
                    ? $"[bold {Theme.Highlight}]{Theme.Pointer}[/] [{Theme.Accent}]{Theme.RadioOn}[/]"
                    : Dim($"{new string(' ', Theme.Pointer.Length)} {Theme.RadioOff}")),
                new Markup(isCurrent ? Accent(label) : Dim(label)),
                new Markup(isCurrent ? summary : Dim(summary)),
                new Markup(isCurrent ? remark : Dim(remark)));
        }

        void Close()
        {
            if (grid is not null)
                body.Add(grid);

            grid = null;
        }

        void AddRow(bool isSetApart, IRenderable pointer, IRenderable label, IRenderable summary, IRenderable remark)
        {
            grid ??= NewGrid(isSetApart);

            if (hasRemarks)
                grid.AddRow(pointer, label, summary, remark);
            else
                grid.AddRow(pointer, label, summary);
        }

        Grid NewGrid(bool isSetApart)
        {
            GridColumn labels = new GridColumn().NoWrap().PadLeft(0).PadRight(3);
            GridColumn summaries = new GridColumn().NoWrap().PadLeft(0).PadRight(hasRemarks ? 3 : 0);

            if (!isSetApart)
            {
                labels.Width = Math.Max(labelWidth, More.Length);

                if (hasRemarks)
                    summaries.Width = summaryWidth;
            }

            Grid created = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadLeft(0).PadRight(1))
                .AddColumn(labels)
                .AddColumn(hasRemarks ? summaries.RightAligned() : summaries);

            return hasRemarks
                ? created.AddColumn(new GridColumn().NoWrap().PadLeft(0).PadRight(0))
                : created;
        }
    }

    /// <summary>
    ///     The explanation of the highlighted choice, and under it what must not be overlooked about it.
    /// </summary>
    /// <param name="choices">All rows.</param>
    /// <param name="current">The highlighted row.</param>
    /// <param name="isSteady">
    ///     Whether the area is as tall as the longest explanation of the list, so the box keeps its height
    ///     while the user moves through the choices; otherwise it is only as tall as the one it shows.
    /// </param>
    private List<IRenderable> BuildExplanation<T>(IReadOnlyList<Choice<T>> choices, int current, bool isSteady)
    {
        Choice<T> shown = choices[current];

        int rows = isSteady
            ? choices.Max(choice => RowsOfText(choice.Hint) + RowsOfText(choice.Caution))
            : RowsOfText(shown.Hint) + RowsOfText(shown.Caution);

        if (rows == 0)
            return [];

        var lines = new List<IRenderable> { new Markup(" ") };

        if (!string.IsNullOrEmpty(shown.Hint))
            lines.Add(new Markup($"[{Theme.Highlight}]{Markup.Escape(shown.Hint)}[/]"));

        if (!string.IsNullOrEmpty(shown.Caution))
            lines.Add(new Markup(Warning(Markup.Escape(shown.Caution))));

        for (int row = RowsOfText(shown.Hint) + RowsOfText(shown.Caution); row < rows; row++)
            lines.Add(new Markup(" "));

        return lines;
    }

    /// <summary>
    ///     How many rows of the box a text takes once it is wrapped to the width of the box.
    /// </summary>
    private int RowsOfText(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : HeightOf(new Markup(Markup.Escape(text)), InnerWidth);

    /// <summary>
    ///     Picks the rows of a list to show in <paramref name="capacity"/> lines. The list moves only as far
    ///     as it takes to keep the highlighted row in sight, the way any list scrolls.
    /// </summary>
    /// <param name="choices">All rows.</param>
    /// <param name="listed">How many of them scroll; the rest stay in sight under the list.</param>
    /// <param name="position">Where the list is; the first row in sight is updated.</param>
    /// <param name="capacity">The lines there is room for.</param>
    /// <param name="detail">How much the list shows besides its rows.</param>
    /// <returns>The first and the last row to show; the last is before the first when there is nothing to show.</returns>
    private static (int First, int Last) Window<T>(IReadOnlyList<Choice<T>> choices,
                                                   int listed,
                                                   ListPosition position,
                                                   int capacity,
                                                   ListDetail detail)
    {
        if (listed == 0)
            return (0, -1);

        // A row under the list is in sight wherever the list is: moving onto it scrolls nothing
        bool isInList = position.Current < listed;

        int first = Math.Clamp(position.First, 0, listed - 1);

        if (isInList && position.Current < first)
            first = position.Current;

        int last = first;

        Extend();

        if (isInList && position.Current > last)
        {
            first = last = position.Current;

            Reach();
        }

        // Room that is left over goes to the rows around: a terminal that grew shows more at once
        Extend();
        Reach();

        position.First = first;

        return (first, last);

        void Extend()
        {
            while (last < listed - 1 && Layout(choices, first, last + 1, listed, detail).Count <= capacity)
                last++;
        }

        void Reach()
        {
            while (first > 0 && Layout(choices, first - 1, last, listed, detail).Count <= capacity)
                first--;
        }
    }

    /// <summary>
    ///     The lines of a list that shows the rows from <paramref name="first"/> to <paramref name="last"/>:
    ///     the rows themselves, the names of their blocks with an empty line on either side, and the marks
    ///     of rows out of sight. The index tells which row a line is, or whose block a heading names.
    /// </summary>
    private static List<(ListLine Kind, int Index)> Layout<T>(IReadOnlyList<Choice<T>> choices,
                                                              int first,
                                                              int last,
                                                              int listed,
                                                              ListDetail detail)
    {
        var lines = new List<(ListLine Kind, int Index)>();

        bool hasNames = detail == ListDetail.Full;
        bool hasMarks = detail >= ListDetail.Marks;

        // The block whose name is already on the screen
        string? named = null;

        if (first > 0 && hasMarks)
        {
            string? group = choices[first].Group;

            // A block that began out of sight keeps its name on the screen: the rows left behind are
            // marked under it, not above it as if they were rows of another block
            if (hasNames && group is not null && group == choices[first - 1].Group)
            {
                lines.Add((ListLine.Heading, first));
                lines.Add((ListLine.Gap, first));

                named = group;
            }

            lines.Add((ListLine.More, first));
        }

        for (int index = first; index <= last; index++)
        {
            string? group = choices[index].Group;

            if (hasNames && group is not null && group != named)
            {
                if (lines.Count > 0)
                    lines.Add((ListLine.Gap, index));

                lines.Add((ListLine.Heading, index));
                lines.Add((ListLine.Gap, index));
            }

            named = group;

            lines.Add((ListLine.Row, index));
        }

        if (last < listed - 1 && hasMarks)
            lines.Add((ListLine.More, last));

        return lines;
    }

    private IReadOnlyList<string> BuildProgress(DownloadProgress progress)
    {
        double share = progress.BytesTotal > 0 ? Math.Clamp((double)progress.BytesDone / progress.BytesTotal, 0, 1) : 0;
        int filled = (int)Math.Round(share * ProgressBarWidth);

        string bar =
            $"[{Theme.Accent}]{Repeat(Theme.BarFull, filled)}[/]" +
            Dim(Repeat(Theme.BarEmpty, ProgressBarWidth - filled)) +
            $"  [bold]{share * 100,3:0}%[/]";

        string details = progress.IsVerifying
            ? Strings.DownloadVerifying
            : Strings.Format(
                nameof(Strings.DownloadDetails),
                Format.Megabytes(progress.BytesDone),
                Format.Megabytes(progress.BytesTotal),
                Format.Megabytes((long)progress.BytesPerSecond));

        return [bar, Dim(Markup.Escape(details))];
    }

    /// <summary>
    ///     A copy of the user's own file as three steps: found on the disk, copying into Echo's folder with how much
    ///     is done, done. The step under way turns where the font has the dots that go round.
    /// </summary>
    internal IReadOnlyList<string> BuildCopy(string fileName, long sizeBytes, DownloadProgress progress, TimeSpan clock)
    {
        long total = progress.BytesTotal > 0 ? progress.BytesTotal : sizeBytes;
        double share = total > 0 ? Math.Clamp((double)progress.BytesDone / total, 0, 1) : 0;

        string done = Strings.Format(nameof(Strings.CopyDetails), Format.Megabytes(progress.BytesDone), Format.Megabytes(total));

        // A mark as wide as the others: the plain theme draws "( )" for a choice not taken
        string waiting = Theme == Theme.Ascii ? Theme.Dot : Theme.RadioOff;

        return
        [
            Good($"{Fill(Strings.CopyFound, fileName)}  {Emphasis(Markup.Escape(Format.Megabytes(sizeBytes)))}"),
            $"[bold {Theme.Highlight}]{Theme.Spinner(clock)}[/] {Markup.Escape(Strings.CopyIntoEcho)}   {Dim(Markup.Escape(done))}  [bold]{share * 100:0}%[/]",
            Dim($"{waiting} {Markup.Escape(Strings.CopyDone)}"),
        ];
    }

    private static List<IRenderable> ToRenderables(IReadOnlyList<string> lines) =>
        [.. lines.Select(line => (IRenderable)new Markup(line.Length == 0 ? " " : line))];

    private static string Repeat(string text, int count) =>
        count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(text, count));

    private static string KeyName(ConsoleKey key) => key switch
    {
        ConsoleKey.Enter => "Enter",
        ConsoleKey.Escape => "Esc",
        ConsoleKey.Spacebar => Strings.KeySpace,
        _ => key.ToString()
    };
}