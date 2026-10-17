using Echo.Setup;
using Serilog.Core;
using Serilog.Events;

namespace Echo.Listening;

/// <summary>
///     The commands typed on the working screen, and what they do.
/// </summary>
/// <param name="activity"><see cref="IListenActivity"/></param>
/// <param name="levelSwitch">The log detail of the running program.</param>
/// <param name="settingsStore">Where a setting changed here is saved, so it stays until the user changes it again.</param>
/// <param name="describeSettings">The main settings Echo runs with, as label and value, for <see cref="Info"/>.</param>
internal sealed class ListenCommands(IListenActivity activity,
                                     LoggingLevelSwitch levelSwitch,
                                     IUserSettingsStore settingsStore,
                                     Func<IReadOnlyList<(string Label, string Value)>> describeSettings)
{
    public const string Setup = "/setup";
    public const string Info = "/info";
    public const string Pause = "/pause";
    public const string Resume = "/resume";
    public const string LogLevel = "/log";
    public const string Help = "/help";
    public const string Quit = "/quit";

    /// <summary>
    ///     The levels of the log, as typed after <see cref="LogLevel"/>, from the most detailed.
    /// </summary>
    public static readonly IReadOnlyList<string> Levels = ["verbose", "debug", "information", "warning", "error", "fatal"];

    private readonly IListenActivity _activity = activity;
    private readonly LoggingLevelSwitch _levelSwitch = levelSwitch;
    private readonly IUserSettingsStore _settingsStore = settingsStore;
    private readonly Func<IReadOnlyList<(string Label, string Value)>> _describeSettings = describeSettings;

    public IReadOnlyList<ListenCommandInfo> All { get; } =
    [
        new(Setup, () => Strings.CommandSetup),
        new(Info, () => Strings.CommandInfo),
        new(Pause, () => Strings.CommandPause),
        new(Resume, () => Strings.CommandResume),
        new(LogLevel, () => Strings.CommandLog, Levels),
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
                IsCurrent: index == (int)_levelSwitch.MinimumLevel)),
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

            case Info:
                AddTable(Strings.ResponseInfoTitle, _describeSettings());

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
            log.Add(EntryKind.Answer, Strings.Format(nameof(Strings.ResponseLogLevelNow), Levels[(int)_levelSwitch.MinimumLevel], all));

            return;
        }

        int index = Levels.ToList().FindIndex(level => level.StartsWith(typed, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            log.Add(EntryKind.Answer, Strings.Format(nameof(Strings.ResponseUnknownLevel), typed, all));

            return;
        }

        _levelSwitch.MinimumLevel = (LogEventLevel)index;

        try
        {
            UserSettings settings = _settingsStore.Load();
            settings.Additional.Set(AdditionalSettings.LogLevel, AdditionalSettings.LogLevel.Steps[index]);
            _settingsStore.Save(settings);

            log.Add(EntryKind.Answer, Strings.Format(nameof(Strings.ResponseLogLevel), Levels[index]));
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            log.Add(EntryKind.Warning, Strings.Format(nameof(Strings.ResponseLogLevelNotSaved), Levels[index], exc.Message));
        }
    }
}