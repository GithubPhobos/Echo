namespace Echo.Listening;

/// <summary>
///     The commands typed on the working screen, and what they do.
/// </summary>
/// <param name="activity"><see cref="IListenActivity"/></param>
/// <param name="levelSwitch">The log detail of the running program.</param>
/// <param name="settingsStore">Where a setting changed here is saved, so it stays until the user changes it again.</param>
/// <param name="insertion">
///     Puts the last recognized text into the clipboard for <see cref="Copy"/>, in turn with the phrases being pasted.
///     Asked for only then: a service that cannot be created, for a setting it cannot take, fails the copying, not the screen.
/// </param>
/// <param name="describeSettings">The main settings Echo runs with, as label and value, for <see cref="Info"/>.</param>
/// <param name="updater">Downloads the newer version for <see cref="Update"/>; without it the command says there is nothing to do.</param>
/// <param name="installCommand">The line that installs Echo, for an Echo that cannot update itself.</param>
internal sealed partial class ListenCommands(IListenActivity activity,
                                             LoggingLevelSwitch levelSwitch,
                                             IUserSettingsStore settingsStore,
                                             Func<ITextInsertionService> insertion,
                                             Func<IReadOnlyList<(string Label, string Value)>> describeSettings,
                                             IUpdater? updater = null,
                                             string installCommand = "")
{
    public const string Setup = "/setup";
    public const string Info = "/info";
    public const string Copy = "/copy";
    public const string Pause = "/pause";
    public const string Resume = "/resume";
    public const string LogLevel = "/log";
    public const string Update = "/update";
    public const string Restart = "/restart";
    public const string Help = "/help";
    public const string Quit = "/quit";

    /// <summary>
    ///     The levels of the log, as typed after <see cref="LogLevel"/>, from the most detailed.
    /// </summary>
    public static readonly IReadOnlyList<string> Levels = ["verbose", "debug", "information", "warning", "error", "fatal"];

    private readonly IListenActivity _activity = activity;
    private readonly LoggingLevelSwitch _levelSwitch = levelSwitch;
    private readonly IUserSettingsStore _settingsStore = settingsStore;
    private readonly Func<ITextInsertionService> _insertion = insertion;
    private readonly Func<IReadOnlyList<(string Label, string Value)>> _describeSettings = describeSettings;
    private readonly IUpdater? _updater = updater;
    private readonly string _installCommand = installCommand;

    /// <summary>
    ///     Done once every <see cref="Copy"/> typed on the screen is through: the screen is left only then, so the
    ///     clipboard does not change behind the next screen, and the services it uses are still there.
    /// </summary>
    public Task Copying { get; private set; } = Task.CompletedTask;

    public IReadOnlyList<ListenCommandInfo> All { get; } =
    [
        new(Setup, () => Strings.CommandSetup),
        new(Info, () => Strings.CommandInfo),
        new(Copy, () => Strings.CommandCopy),
        new(Pause, () => Strings.CommandPause),
        new(Resume, () => Strings.CommandResume),
        new(LogLevel, () => Strings.CommandLog, Levels),
        new(Update, () => Strings.CommandUpdate),
        new(Restart, () => Strings.CommandRestart),
        new(Help, () => Strings.CommandHelp),
        new(Quit, () => Strings.CommandQuit),
    ];

    /// <summary>
    ///     The values a command is chosen with, the one in effect marked.
    /// </summary>
    public IReadOnlyList<PanelRow> Options(ListenCommandInfo command)
    {
        if (command.Name != LogLevel)
            return [];

        string[] descriptions =
        [
            Strings.LevelVerbose, Strings.LevelDebug, Strings.LevelInformation,
            Strings.LevelWarning, Strings.LevelError, Strings.LevelFatal,
        ];

        return
        [
            .. Levels.Select((level, index) => new PanelRow(
                $"{LogLevel} {level}",
                level,
                descriptions[index],
                IsCurrent: index == (int)_levelSwitch.MinimumLevel,
                LabelStyle: ListenFrame.LevelStyle(level))),
        ];
    }

    /// <summary>
    ///     Runs a typed line. What it answers goes to the screen's log, under the line itself.
    /// </summary>
    public ListenOutcome Run(string line)
    {
        ScreenLog log = _activity.Log;

        if (line.Length == 0)
            return ListenOutcome.Continue;

        log.Add(EntryKind.Command, line);

        if (line[0] != CommandLine.Prefix)
        {
            log.Add(EntryKind.Answer, Strings.ResponseNotACommand);

            return ListenOutcome.Continue;
        }

        string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string name = words[0].ToLowerInvariant();

        switch (name)
        {
            case Setup:
                return ListenOutcome.Setup;

            case Quit:
                return ListenOutcome.Quit;

            case Restart:
                return ListenOutcome.Restart;

            case Update:
                StartUpdate();

                break;

            case Info:
                AddTable(Strings.ResponseInfoTitle, _describeSettings());

                break;

            case Copy:
                Copying = Task.WhenAll(Copying, CopyLastPhraseAsync());

                break;

            case Pause:
                _activity.IsPaused = true;
                log.Add(EntryKind.Answer, Strings.ResponsePaused);

                break;

            case Resume:
                _activity.IsPaused = false;
                log.Add(EntryKind.Answer, Strings.ResponseResumed);

                break;

            case LogLevel:
                ChangeLevel(words.Length > 1 ? words[1] : null);

                break;

            case Help:
                AddTable(
                    Strings.ResponseHelpTitle,
                    [.. All.Select(command => (command.Arguments is null ? command.Name : $"{command.Name} {Strings.CommandLogArgument}", command.Description()))]);

                break;

            default:
                log.Add(EntryKind.Answer, Strings.Format(nameof(Strings.ResponseUnknownCommand), name));

                break;
        }

        return ListenOutcome.Continue;
    }

    /// <summary>
    ///     Writes what the updater has to tell into the log, in the interface language.
    /// </summary>
    public static void Tell(ScreenLog log, UpdateNotice notice)
    {
        switch (notice.Kind)
        {
            case UpdateNoticeKind.DownloadStarted:
                log.Add(EntryKind.Answer, Strings.Format(nameof(Strings.ResponseUpdateStarted), notice.Version));

                break;

            case UpdateNoticeKind.Downloaded:
                log.Add(EntryKind.Answer, Strings.Format(nameof(Strings.UpdateDownloaded), notice.Version));

                break;

            case UpdateNoticeKind.DownloadFailed:
                log.Add(EntryKind.Warning, Strings.Format(nameof(Strings.UpdateDownloadFailed), notice.Version, notice.Reason));

                break;

            case UpdateNoticeKind.UpToDate:
                log.Add(EntryKind.Answer, Strings.Format(nameof(Strings.ResponseUpdateLatest), notice.Version));

                break;

            case UpdateNoticeKind.CheckFailed:
                log.Add(EntryKind.Warning, Strings.Format(nameof(Strings.UpdateCheckFailed), notice.Reason));

                break;
        }
    }

    /// <summary>
    ///     What /info says about the version: the one in use and what there is about a newer one.
    /// </summary>
    public static string DescribeVersion(UpdateStatus status) => status.Stage switch
    {
        UpdateStage.Latest => Strings.Format(nameof(Strings.InfoVersionLatest), status.Installed),
        UpdateStage.Available => Strings.Format(nameof(Strings.InfoVersionAvailable), status.Installed, status.Newer),
        UpdateStage.Downloading => Strings.Format(nameof(Strings.InfoVersionDownloading), status.Installed, status.Newer, status.Percent),
        UpdateStage.Downloaded => Strings.Format(nameof(Strings.InfoVersionDownloaded), status.Installed, status.Newer),
        _ => status.Installed
    };

    /// <summary>
    ///     Downloads the newer version, or says why there is nothing to download.
    /// </summary>
    private void StartUpdate()
    {
        ScreenLog log = _activity.Log;

        if (_updater is null)
        {
            log.Add(EntryKind.Answer, Strings.Format(nameof(Strings.ResponseUpdateNotInstalled), _installCommand));

            return;
        }

        UpdateStatus status = _updater.Status;

        string answer = _updater.Download() switch
        {
            UpdateStart.Started => Strings.Format(nameof(Strings.ResponseUpdateStarted), _updater.Status.Newer),
            UpdateStart.Checking => Strings.ResponseUpdateChecking,
            UpdateStart.AlreadyDownloading => Strings.Format(nameof(Strings.ResponseUpdateDownloading), _updater.Status.Percent),
            UpdateStart.AlreadyDownloaded => Strings.Format(nameof(Strings.ResponseUpdateReady), status.Newer),
            UpdateStart.UpToDate => Strings.Format(nameof(Strings.ResponseUpdateLatest), status.Installed),
            _ => Strings.Format(nameof(Strings.ResponseUpdateNotInstalled), _installCommand)
        };

        log.Add(EntryKind.Answer, answer);
    }

    /// <summary>
    ///     A title and rows of two columns, the first one as wide as its widest cell.
    /// </summary>
    private void AddTable(string title, IReadOnlyList<(string Label, string Value)> rows)
    {
        ScreenLog log = _activity.Log;
        int width = rows.Count == 0 ? 0 : rows.Max(row => ListenFrame.WidthOf(row.Label));

        log.Add(EntryKind.Answer, title);

        foreach ((string label, string value) in rows)
        {
            log.Add(new ScreenEntry(
                DateTimeOffset.Now,
                EntryKind.Answer,
                $"  {label}{new string(' ', width - ListenFrame.WidthOf(label))}   {value}",
                Indent: 2 + width + 3));
        }
    }

    private void ChangeLevel(string? typed)
    {
        ScreenLog log = _activity.Log;
        string all = string.Join(", ", Levels);

        if (typed is null)
        {
            log.Add(WithLevels(Strings.Format(nameof(Strings.ResponseLogLevelNow), Levels[(int)_levelSwitch.MinimumLevel], all)));

            return;
        }

        int index = Levels.ToList().FindIndex(level => level.StartsWith(typed, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            log.Add(WithLevels(Strings.Format(nameof(Strings.ResponseUnknownLevel), typed, all)));

            return;
        }

        _levelSwitch.MinimumLevel = (LogEventLevel)index;

        try
        {
            UserSettings settings = _settingsStore.Load();
            settings.Additional.Set(AdditionalSettings.LogLevel, AdditionalSettings.LogLevel.Steps[index]);
            _settingsStore.Save(settings);

            log.Add(WithLevels(Strings.Format(nameof(Strings.ResponseLogLevel), Levels[index])));
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            log.Add(EntryKind.Warning, Strings.Format(nameof(Strings.ResponseLogLevelNotSaved), Levels[index], exc.Message));
        }
    }

    /// <summary>
    ///     An answer in which every level named is drawn in the color of that level, so the levels are told apart at a glance.
    /// </summary>
    private static ScreenEntry WithLevels(string answer) =>
        new(DateTimeOffset.Now, EntryKind.Answer, answer)
        {
            Accents = [.. LevelName().Matches(answer).Select(match => (match.Index, match.Length, ListenFrame.LevelStyle(match.Value)))],
        };

    /// <summary>
    ///     Puts the phrase recognized last into the clipboard, for when it went into the wrong window or nowhere.
    /// </summary>
    private async Task CopyLastPhraseAsync()
    {
        ScreenLog log = _activity.Log;

        if (log.Snapshot().LastOrDefault(entry => entry.Kind == EntryKind.Dictated) is not { } phrase)
        {
            log.Add(EntryKind.Answer, Strings.ResponseNothingToCopy);

            return;
        }

        try
        {
            await _insertion().CopyAsync(phrase.Text);

            log.Add(EntryKind.Answer, Strings.ResponseCopied);
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            log.Add(EntryKind.Warning, Strings.Format(nameof(Strings.ResponseCopyFailed), exc.Message));
        }
    }

    [GeneratedRegex(@"\b(verbose|debug|information|warning|error|fatal)\b")]
    private static partial Regex LevelName();
}