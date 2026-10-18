using Echo.UI;
using Spectre.Console;

namespace Echo.Listening;

/// <summary>
///     The working screen: it takes the whole terminal while Echo runs, shows what Echo does and lets the user
///     type commands. When it is left, the terminal looks the way it did before.
/// </summary>
/// <param name="console">The console to draw on.</param>
/// <param name="theme">The symbols and colors to draw with.</param>
/// <param name="activity"><see cref="IListenActivity"/></param>
/// <param name="commands"><see cref="ListenCommands"/></param>
/// <param name="info">What the state line says about the settings.</param>
/// <param name="tick">How often the screen is refreshed.</param>
/// <param name="focusReports">
///     Where the terminal is asked to say when its window becomes active and stops being active; null where
///     the system itself tells (Windows).
/// </param>
internal sealed class ListenScreen(IAnsiConsole console,
                                  Theme theme,
                                  IListenActivity activity,
                                  ListenCommands commands,
                                  ListenInfo info,
                                  TimeSpan tick,
                                  TerminalFocusReports? focusReports = null)
{
    private const string EnterAlternateScreen = "\u001b[?1049h";
    private const string LeaveAlternateScreen = "\u001b[?1049l";

    /// <summary>
    ///     Asks the terminal to turn the mouse wheel into arrow keys, which scroll the log: a screen of its own
    ///     has no scrollback for the wheel to move.
    /// </summary>
    private const string WheelAsArrows = "\u001b[?1007h";

    private const string HideCursor = "\u001b[?25l";
    private const string ShowCursor = "\u001b[?25h";
    private const string ClearToLineEnd = "\u001b[K";
    private const string ClearScreen = "\u001b[2J";

    private readonly IAnsiConsole _console = console;
    private readonly IListenActivity _activity = activity;
    private readonly ListenCommands _commands = commands;
    private readonly ListenInfo _info = info;
    private readonly TimeSpan _tick = tick;
    private readonly TerminalFocusReports? _focusReports = focusReports;
    private readonly ListenFrame _frame = new(theme);
    private readonly CommandLine _line = new(commands.All);
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>
    ///     How many rows the log is scrolled back.
    /// </summary>
    private int _scroll;

    /// <summary>
    ///     While the log is scrolled back it stands still: lines that arrive meanwhile are shown once
    ///     the user comes back down. This is the log as it was when it was scrolled, kept whole, so that
    ///     the oldest lines leaving the log meanwhile do not move it either.
    /// </summary>
    private IReadOnlyList<ScreenEntry>? _frozen;

    private int _logRows = 1;

    /// <summary>
    ///     How much faster than a row per arrow the log moves while the arrows keep coming.
    /// </summary>
    private const double ArrowSpeed = 1.5;

    /// <summary>
    ///     A longer pause between arrows starts the scrolling anew, one row per press.
    /// </summary>
    private static readonly TimeSpan ArrowPause = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     The part of a row the arrows so far have earned but not yet moved.
    /// </summary>
    private double _arrowCarry;

    private int _arrowDirection;

    private TimeSpan _lastArrow;

    /// <summary>
    ///     The command whose value is being chosen from the panel, if any.
    /// </summary>
    private ListenCommandInfo? _choosing;

    /// <summary>
    ///     The highlighted row of the panel.
    /// </summary>
    private int _selected;

    /// <summary>
    ///     The rows written last, so that only the rows that changed are written again.
    /// </summary>
    private string[] _written = [];

    private (int Width, int Height) _size;

    /// <summary>
    ///     Runs the screen until the user leaves it.
    /// </summary>
    public async Task<ListenOutcome> RunAsync(CancellationToken cancellationToken)
    {
        bool? treatedControlC = SetControlCAsInput(true);

        Write(EnterAlternateScreen + WheelAsArrows + HideCursor);

        if (_focusReports is not null)
        {
            _focusReports.Started();
            Write(TerminalFocusReports.Enable);
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (_console.Input.IsKeyAvailable())
                {
                    ConsoleKeyInfo? key = await _console.Input.ReadKeyAsync(intercept: true, cancellationToken);

                    // A report of the terminal about its window, not a key of the user
                    if (key is ConsoleKeyInfo report && _focusReports?.Handle(report) == true)
                        continue;

                    if (key is ConsoleKeyInfo pressed && Handle(pressed) is var outcome && outcome != ListenOutcome.Continue)
                        return outcome;

                    // Every key shows its effect at once, even when several came together
                    Draw();
                }

                Draw();

                await Task.Delay(_tick, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_focusReports is not null)
            {
                Write(TerminalFocusReports.Disable);
                _focusReports.Stopped();
            }

            Write(ShowCursor + LeaveAlternateScreen);

            if (treatedControlC is bool previous)
                SetControlCAsInput(previous);
        }

        return ListenOutcome.Quit;
    }

    /// <summary>
    ///     The rows of the panel under the command line: the values of a command being chosen,
    ///     or the commands that match what is typed.
    /// </summary>
    private IReadOnlyList<PanelRow> Panel =>
        _choosing is not null ? _commands.Options(_choosing) : _line.Suggestions;

    /// <summary>
    ///     Builds the screen for the current moment and writes the rows that differ from what is on the terminal.
    /// </summary>
    internal void Draw()
    {
        // Back at the bottom, the log shows what it has now
        if (_scroll == 0)
            _frozen = null;

        IReadOnlyList<ScreenEntry> entries = _frozen ?? _activity.Log.Snapshot();

        ListenState state = _activity.State;
        IReadOnlyList<PanelRow> panel = Panel;

        _selected = panel.Count == 0 ? 0 : Math.Clamp(_selected, 0, panel.Count - 1);

        var view = new ListenView(
            state,
            state == ListenState.Recording ? DateTimeOffset.Now - _activity.RecordingStarted : TimeSpan.Zero,
            _clock.Elapsed,
            _activity.RecentLevels(ListenFrame.EqualizerWidth),
            _info.Model,
            _info.Key,
            entries,
            _scroll,
            _line.Text,
            panel,
            _selected)
        {
            SilenceLevel = _info.SilenceLevel,
        };

        (int Width, int Height) size = (_console.Profile.Width, _console.Profile.Height);

        ListenFrameResult result = _frame.Build(view, size.Width, size.Height);

        _logRows = result.LogRows;
        _scroll = result.ScrollRows;

        if (_scroll == 0)
            _frozen = null;

        // A terminal of another size is drawn anew; otherwise only the rows that changed are written
        if (size != _size)
        {
            Write(ClearScreen);
            _written = [];
            _size = size;
        }

        for (int index = 0; index < result.Lines.Count; index++)
        {
            if (index < _written.Length && _written[index] == result.Lines[index])
                continue;

            // Every row is put in its place explicitly: nothing depends on how the terminal moves to the next one
            Write($"\u001b[{index + 1};1H");

            if (result.Lines[index].Length > 0)
                _console.Write(new Markup(result.Lines[index]));

            Write(ClearToLineEnd);
        }

        _written = [.. result.Lines];
    }

    internal ListenOutcome Handle(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            return ListenOutcome.Quit;

        IReadOnlyList<PanelRow> panel = Panel;

        switch (key.Key)
        {
            case ConsoleKey.Enter:
                return panel.Count > 0 ? Take(panel[Math.Clamp(_selected, 0, panel.Count - 1)]) : Run(_line.Submit());

            case ConsoleKey.UpArrow when panel.Count > 0:
                _selected = (_selected + panel.Count - 1) % panel.Count;

                break;

            case ConsoleKey.DownArrow when panel.Count > 0:
                _selected = (_selected + 1) % panel.Count;

                break;

            case ConsoleKey.UpArrow:
                Scroll(ArrowRows(1));

                break;

            case ConsoleKey.DownArrow:
                Scroll(-ArrowRows(-1));

                break;

            case ConsoleKey.PageUp:
                Scroll(Math.Max(1, _logRows - 1));

                break;

            case ConsoleKey.PageDown:
                Scroll(-Math.Max(1, _logRows - 1));

                break;

            case ConsoleKey.End:
                _scroll = 0;

                break;

            case ConsoleKey.Escape:
                _choosing = null;
                _line.Clear();
                _selected = 0;

                break;

            // Ctrl+Backspace takes away the whole word, and so does Ctrl+W, as in Linux terminals
            case ConsoleKey.Backspace or ConsoleKey.W when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                _choosing = null;
                _line.DeleteWord();
                _selected = 0;

                break;

            case ConsoleKey.Backspace:
                _choosing = null;
                _line.Backspace();
                _selected = 0;

                break;

            default:
                if (!char.IsControl(key.KeyChar))
                {
                    _choosing = null;
                    _line.Type(key.KeyChar);
                    _selected = 0;
                }

                break;
        }

        return ListenOutcome.Continue;
    }

    /// <summary>
    ///     Takes the highlighted row of the panel. A command that needs a value opens the list of its values,
    ///     with the one in effect highlighted; anything else runs at once.
    /// </summary>
    private ListenOutcome Take(PanelRow row)
    {
        if (_choosing is null && _line.Command(row.Text) is { Arguments: not null } command)
        {
            _choosing = command;
            _line.Replace(command.Name);

            IReadOnlyList<PanelRow> options = _commands.Options(command);
            _selected = Math.Max(0, options.ToList().FindIndex(option => option.IsCurrent));

            return ListenOutcome.Continue;
        }

        _choosing = null;
        _line.Clear();
        _selected = 0;

        return Run(row.Text);
    }

    private ListenOutcome Run(string line)
    {
        // What was typed is answered at the end of the log, so the log goes back there
        _scroll = 0;
        _frozen = null;

        return _commands.Run(line);
    }

    /// <summary>
    ///     How many rows one arrow moves the log. A single press moves it by one row; while the arrows keep
    ///     coming, as they do from a held key or the mouse wheel, every one is worth a bit more than a row,
    ///     so long scrolling goes <see cref="ArrowSpeed"/> times faster.
    /// </summary>
    /// <param name="direction">1 for up, -1 for down.</param>
    internal int ArrowRows(int direction)
    {
        TimeSpan now = _clock.Elapsed;

        if (direction != _arrowDirection || now - _lastArrow > ArrowPause)
            _arrowCarry = 0;

        _arrowDirection = direction;
        _lastArrow = now;

        double rows = 1 + _arrowCarry;
        int whole = (int)rows;

        _arrowCarry = rows - whole + (ArrowSpeed - 1);

        return whole;
    }

    /// <summary>
    ///     Moves the log by <paramref name="rows"/>: up when positive, down when negative.
    /// </summary>
    private void Scroll(int rows)
    {
        if (_scroll == 0 && rows > 0)
            _frozen = _activity.Log.Snapshot();

        _scroll = Math.Max(0, _scroll + rows);
    }

    private void Write(string controlCodes) =>
        _console.Write(new ControlCode(controlCodes));

    /// <summary>
    ///     Makes Ctrl+C arrive as a key instead of stopping the process at once: the screen has to give
    ///     the terminal back first. Returns what it was before, or null when there is no console to ask.
    /// </summary>
    private static bool? SetControlCAsInput(bool value)
    {
        try
        {
            bool previous = Console.TreatControlCAsInput;
            Console.TreatControlCAsInput = value;

            return previous;
        }
        catch (IOException)
        {
            return null;
        }
    }
}