using System.Globalization;
using Echo.UI;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Echo.Listening;

/// <summary>
///     Lays out the working screen: the state on top, the log, the command line, the panel of choices
///     and the keys at the bottom. Every row is made to fit the terminal's width, so nothing wraps on its own.
/// </summary>
/// <param name="theme">The symbols and colors to draw with.</param>
internal sealed class ListenFrame(Theme theme)
{
    /// <summary>
    ///     How many columns the equalizer takes in the state line.
    /// </summary>
    public const int EqualizerWidth = 12;

    /// <summary>
    ///     Stands in the place of the timer while nothing is recorded.
    /// </summary>
    private const string NoTime = "--:--";

    /// <summary>
    ///     How many arcs spread to each side of the ear while the user speaks.
    /// </summary>
    private const int MaxArcs = 5;

    /// <summary>
    ///     The color of the model in the state line.
    /// </summary>
    private const string ModelColor = "#79c0ff";

    /// <summary>
    ///     The color of ordinary lines of the log, lighter than <see cref="Muted"/>: the log is there to be read.
    /// </summary>
    private const string LogText = "#c9d1d9";

    /// <summary>
    ///     The color of the time of a log line and of a step that is under way.
    /// </summary>
    private const string LogQuiet = "#8b949e";

    /// <summary>
    ///     How many of the latest pieces of sound, 50 ms each, the arcs follow.
    /// </summary>
    private const int SmoothedPieces = 3;

    /// <summary>
    ///     The color of what the recognition library says about itself, such as how it found the graphics card:
    ///     fainter than Echo's own lines, since it is there only for whoever looks into a problem.
    /// </summary>
    private const string LibraryText = "#6e7681";

    /// <summary>
    ///     The color of the still dots shown while nothing is recorded.
    /// </summary>
    private const string IdleDots = "#3d4450";

    /// <summary>
    ///     The color of the lines of the tree that holds the steps of a group.
    /// </summary>
    private const string TreeLines = "#484f58";

    /// <summary>
    ///     The most rows the panel under the command line shows.
    /// </summary>
    public const int MaxPanelRows = 8;

    /// <summary>
    ///     The smallest terminal the screen is laid out for. A smaller one shows only a request to make it larger.
    /// </summary>
    public const int MinWidth = 40;

    /// <inheritdoc cref="MinWidth"/>
    public const int MinHeight = 10;

    /// <summary>
    ///     The rows that are always there besides the log: the state, the rule, an empty row,
    ///     the three rows of the command box and the keys.
    /// </summary>
    private const int FixedRows = 7;

    /// <summary>
    ///     The color of ordinary text: quieter than what matters, but easy to read.
    /// </summary>
    public const string Muted = "#a3abb5";

    /// <summary>
    ///     How far a path that goes onto its own row is set in under its label.
    /// </summary>
    private const int PathIndent = 2;

    /// <summary>
    ///     "  21:03  ": the time column of the log.
    /// </summary>
    private const int TimeWidth = 9;

    private readonly Theme _theme = theme;

    private bool IsAscii => _theme == Theme.Ascii;

    /// <summary>
    ///     The ear the sound comes to, in the middle of the equalizer while a recording goes on:
    ///     the emoji of two cells where the terminal draws emoji, "@" elsewhere.
    /// </summary>
    private string Ear => _theme == Theme.Full ? "\U0001F442" : "@";

    /// <summary>
    ///     What stands between the parts of the state line and between the keys: a dashed line,
    ///     or a plain one where the font has no dashed one.
    /// </summary>
    private string Separator => _theme == Theme.Full ? "  ┊  " : IsAscii ? "  |  " : "  │  ";

    /// <summary>
    ///     Lays out a screen of <paramref name="width"/> columns and <paramref name="height"/> rows.
    ///     The last row of the terminal is left empty, so writing the screen never scrolls it.
    /// </summary>
    public ListenFrameResult Build(ListenView view, int width, int height)
    {
        if (width < MinWidth || height < MinHeight)
            return BuildTooSmall(view, width, height);

        int rows = height - 1;
        int inner = width - 1;

        // The panel takes what is left after the other rows and a row of the log, and an empty row under it;
        // it scrolls with its highlighted row when it has more rows than it shows
        int panelRoom = Math.Max(0, rows - FixedRows - 1 - 1);
        int shownPanel = Math.Min(Math.Min(view.Panel.Count, MaxPanelRows), panelRoom);
        int panelStart = Math.Clamp(view.Selected - shownPanel + 1, 0, Math.Max(0, view.Panel.Count - shownPanel));

        // Above the log: the state and the rule right under it; the log starts right under the rule.
        // Below it: an empty row, the command box, the panel with an empty row under it, the keys
        int below = 1 + 3 + (shownPanel > 0 ? shownPanel + 1 : 0) + 1;
        int logRows = Math.Max(1, rows - 2 - below);

        (List<string> log, int scroll) = BuildLog(view.Entries, view.ScrollRows, logRows, inner);

        var lines = new List<string>
        {
            BuildState(view, inner),
            BuildRule(scroll > 0, inner),
        };

        lines.AddRange(log);
        lines.Add(string.Empty);
        lines.AddRange(BuildInput(view.Input, inner));

        if (shownPanel > 0)
        {
            for (int index = panelStart; index < panelStart + shownPanel; index++)
                lines.Add(BuildPanelRow(view.Panel, index, isSelected: index == view.Selected, inner));

            lines.Add(string.Empty);
        }

        lines.Add(BuildKeys(isPanelOpen: shownPanel > 0, inner));

        return new ListenFrameResult(lines, logRows, scroll);
    }

    /// <summary>
    ///     A terminal too small for the screen asks to be made larger. Dictation goes on meanwhile.
    /// </summary>
    private static ListenFrameResult BuildTooSmall(ListenView view, int width, int height)
    {
        List<string> lines =
        [
            .. Wrap(Strings.ListenTooSmall, Math.Max(1, width - 1))
                .Take(Math.Max(1, height - 1))
                .Select(line => $"[{Theme.Warning}]{Markup.Escape(line.TrimEnd())}[/]"),
        ];

        return new ListenFrameResult(lines, LogRows: 1, ScrollRows: view.ScrollRows);
    }

    /// <summary>
    ///     How many terminal cells a plain text takes.
    /// </summary>
    public static int WidthOf(string text) => new Segment(text).CellCount();

    /// <summary>
    ///     What the state line says about the state: the state on the left, the time of the recording on
    ///     the right, "--:--" while there is none. Always as wide as its widest variant, so the timer,
    ///     the equalizer and everything after them stay in place.
    /// </summary>
    private (string Markup, int Width) State(ListenView view)
    {
        bool isRecording = view.State == ListenState.Recording;

        string time = isRecording
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)view.RecordingTime.TotalMinutes:00}:{view.RecordingTime.Seconds:00}")
            : NoTime;

        (string mark, string text, string color) = view.State switch
        {
            ListenState.Listening => (IsAscii ? "o" : "○", Strings.ListenListening, Theme.Success),
            ListenState.Recording => (_theme.Record, Strings.ListenRecording, Theme.Failure),
            ListenState.Transcribing => (_theme.Spinner(view.Clock), Strings.ListenTranscribing, Theme.Highlight),
            ListenState.Paused => (IsAscii ? "||" : "‖", Strings.ListenPaused, Theme.Warning),
            ListenState.Failed => (_theme.Bad, Strings.ListenFailed, Theme.Failure),
            _ => (IsAscii ? "o" : "○", Strings.ListenStarting, Muted)
        };

        string[] all =
        [
            Strings.ListenListening, Strings.ListenRecording, Strings.ListenTranscribing,
            Strings.ListenPaused, Strings.ListenFailed, Strings.ListenStarting,
        ];

        int width = 2 + all.Max(WidthOf) + 1 + NoTime.Length;
        string plain = $"{mark} {text}";
        string gap = new(' ', Math.Max(1, width - WidthOf(plain) - NoTime.Length));

        string timer = isRecording ? $"[bold {color}]{time}[/]" : SetupUi.Dim(time);

        return ($"[bold {color}]{Markup.Escape(plain)}[/]{gap}{timer}", width);
    }

    private string BuildState(ListenView view, int width)
    {
        (string state, int stateWidth) = State(view);

        string separator = Separator;

        string gap = SetupUi.Dim(Markup.Escape(separator));
        int gapWidth = WidthOf(separator);

        (string Markup, int Width) equalizer = (gap + Equalizer(view), gapWidth + EqualizerWidth);
        (string Markup, int Width) key = ($"{gap}[bold {Theme.Highlight}]{Markup.Escape(view.Key)}[/]", gapWidth + WidthOf(view.Key));
        (string Markup, int Width) model = ($"{gap}[bold {ModelColor}]{Markup.Escape(view.Model)}[/]", gapWidth + WidthOf(view.Model));

        List<(string Markup, int Width)> pieces = [("  ", 2), (state, stateWidth), equalizer, key, model];

        // What does not fit is left out whole, since a piece cut in the middle reads as something else:
        // the model first, which /info shows too, then the key, then the equalizer
        foreach ((string Markup, int Width) piece in new[] { model, key, equalizer })
        {
            if (pieces.Sum(candidate => candidate.Width) <= width)
                break;

            pieces.Remove(piece);
        }

        return string.Concat(pieces.Select(piece => piece.Markup));
    }

    /// <summary>
    ///     While a recording goes on: a point the sound comes from, with arcs spreading from it to both
    ///     sides, more of them the louder the voice. Otherwise a row of still dots: nothing is being recorded.
    /// </summary>
    private string Equalizer(ListenView view)
    {
        if (view.State == ListenState.Recording)
            return Waves(Loudness(view.Levels, view.SilenceLevel));

        // Recognizing: the point stays, the arcs are gone
        if (view.State == ListenState.Transcribing)
            return Waves(0);

        return $"[{IdleDots}]{Markup.Escape(Repeat(_theme.Dot, EqualizerWidth))}[/]";
    }

    /// <summary>
    ///     "··(((@)))···": the ear in the middle of the dots of the still equalizer, and up to
    ///     <see cref="MaxArcs"/> arcs on each side of it, brighter near the ear. Where there is no arc,
    ///     the dot stays, so silence looks like the equalizer at rest.
    /// </summary>
    private string Waves(int arcs)
    {
        string dot = $"[{IdleDots}]{Markup.Escape(_theme.Dot)}[/]";
        var line = new StringBuilder();

        for (int index = 0; index < MaxArcs; index++)
        {
            // The arcs nearest to the ear light up first
            bool isLit = index >= MaxArcs - arcs;
            double nearness = index / (double)(MaxArcs - 1);

            line.Append(isLit ? $"[bold {ArcColor(nearness)}]([/]" : dot);
        }

        line.Append($"[bold {Theme.Highlight}]{Ear}[/]");

        for (int index = 0; index < MaxArcs; index++)
        {
            bool isLit = index < arcs;
            double nearness = 1 - index / (double)(MaxArcs - 1);

            line.Append(isLit ? $"[bold {ArcColor(nearness)}])[/]" : dot);
        }

        // Where the ear takes one cell, the arcs and the ear take one less than the equalizer: a dot fills it
        for (int column = 2 * MaxArcs + WidthOf(Ear); column < EqualizerWidth; column++)
            line.Append(dot);

        return line.ToString();
    }

    /// <summary>
    ///     How many arcs the voice is worth: the loudest of the latest pieces of sound, so the arcs do not
    ///     flicker between syllables. Nothing above the silence level, nothing that would be recorded: no arcs.
    /// </summary>
    private static int Loudness(IReadOnlyList<float> levels, float silenceLevel)
    {
        float level = levels.Count == 0 ? 0f : levels.Skip(Math.Max(0, levels.Count - SmoothedPieces)).Max();

        if (level <= silenceLevel)
            return 0;

        // Speech rarely exceeds a third of the full scale, so the scale is stretched towards quiet sounds,
        // the way the meter of the setup wizard is
        return Math.Max(1, (int)Math.Round(Math.Sqrt(Math.Clamp(level * 3f, 0f, 1f)) * MaxArcs));
    }

    /// <summary>
    ///     The color of an arc: blue far from the ear, the purple and then the pink of the logo next to it.
    /// </summary>
    /// <param name="nearness">1 next to the ear, 0 farthest from it.</param>
    private static string ArcColor(double nearness)
    {
        (int R, int G, int B)[] stops = [(0x79, 0xc0, 0xff), (0xc0, 0x84, 0xfc), (0xff, 0x7a, 0xc6)];

        double position = Math.Clamp(nearness, 0, 1) * (stops.Length - 1);
        int index = Math.Min(stops.Length - 2, (int)position);
        double share = position - index;

        ((int r1, int g1, int b1), (int r2, int g2, int b2)) = (stops[index], stops[index + 1]);

        int Mix(int from, int to) => (int)Math.Round(from + (to - from) * share);

        return $"#{Mix(r1, r2):x2}{Mix(g1, g2):x2}{Mix(b1, b2):x2}";
    }

    /// <summary>
    ///     A color between the two colors of the logo, dimmed towards the background by <paramref name="brightness"/>.
    /// </summary>
    private static string Gradient(double share, double brightness)
    {
        (int r1, int g1, int b1) = (0xc0, 0x84, 0xfc);
        (int r2, int g2, int b2) = (0xff, 0x7a, 0xc6);
        (int r0, int g0, int b0) = (0x0d, 0x11, 0x17);

        int Mix(int from, int to, int background) =>
            (int)Math.Round(background + (from + (to - from) * share - background) * brightness);

        return $"#{Mix(r1, r2, r0):x2}{Mix(g1, g2, g0):x2}{Mix(b1, b2, b0):x2}";
    }

    private string BuildRule(bool isScrolled, int width)
    {
        string dash = IsAscii ? "-" : "─";

        if (!isScrolled)
            return SetupUi.Dim(Repeat(dash, width));

        // Scrolled back, the rule says how to get back to the end
        string note = $" {Strings.ListenScrolled} ";
        int left = Math.Max(0, width - WidthOf(note) - 2);

        return $"{SetupUi.Dim(Repeat(dash, left))}[{Theme.Highlight}]{Markup.Escape(note)}[/]{SetupUi.Dim(Repeat(dash, 2))}";
    }

    /// <summary>
    ///     The rows of the log that are in sight, from the top, padded to <paramref name="logRows"/>.
    /// </summary>
    private (List<string> Rows, int Scroll) BuildLog(IReadOnlyList<ScreenEntry> entries, int scroll, int logRows, int width)
    {
        // Only as many entries are laid out as it takes to fill the screen at the scrolled position
        var rows = new List<string>();

        for (int index = entries.Count - 1; index >= 0 && rows.Count < scroll + logRows; index--)
            rows.InsertRange(0, Rows(entries[index], width, index + 1 < entries.Count ? entries[index + 1] : null));

        int effective = Math.Clamp(scroll, 0, Math.Max(0, rows.Count - logRows));
        int end = rows.Count - effective;
        int start = Math.Max(0, end - logRows);

        List<string> visible = rows.GetRange(start, end - start);

        while (visible.Count < logRows)
            visible.Add(string.Empty);

        return (visible, effective);
    }

    /// <summary>
    ///     One log line laid out in rows: the time, a mark for what matters, the text wrapped under itself.
    ///     A step of a group hangs on the lines of a tree under its heading; <paramref name="next"/> tells
    ///     whether it is the last step, which ends the tree.
    /// </summary>
    public List<string> Rows(ScreenEntry entry, int width, ScreenEntry? next = null)
    {
        string time = $"  {entry.Time.LocalDateTime:HH:mm}  ";

        (string mark, string markColor, string textStyle) = entry.Kind switch
        {
            EntryKind.Warning => ("! ", Theme.Warning, Theme.Warning),
            EntryKind.Error => ($"{_theme.Bad} ", Theme.Failure, Theme.Failure),
            EntryKind.Dictated => (IsAscii ? "> " : "└ ", Theme.Highlight, "bold #f0f6fc"),
            EntryKind.Command => (string.Empty, Theme.Highlight, Theme.Highlight),
            EntryKind.Message when entry.Tone == MessageTone.Heading => (HeadingMark, $"bold {Theme.Highlight}", ToneStyle(entry.Tone)),
            EntryKind.Message => (Branch(entry, next), TreeLines, ToneStyle(entry.Tone)),
            _ => (string.Empty, LogText, LogText)
        };

        int indent = TimeWidth + WidthOf(mark);
        int room = Math.Max(10, width - indent);

        string text = Printable(entry.Note is null ? entry.Text : $"{entry.Text}  {entry.Note}");

        // A row of a table is a single line: a line break inside a value, such as a device name, is a space
        if (entry.Indent > 0)
            text = text.Replace('\n', ' ');

        List<string> chunks = WrapLines(text, room);

        int continuation = entry.Indent > 0 && room - entry.Indent >= 10 ? entry.Indent : 0;

        // A path that does not fit beside its label goes whole onto the next row, set in a little,
        // so that it can be selected and copied in one piece
        if (entry.Indent > 0 && chunks.Count > 1 && entry.Indent < text.Length && Path.IsPathFullyQualified(text[entry.Indent..]))
        {
            continuation = PathIndent;
            chunks = [text[..entry.Indent], .. Wrap(text[entry.Indent..], room - PathIndent, atSpaces: false)];
        }

        // Any other row of a table continues under its value, not under its label; a value with no spaces
        // to break at is broken wherever the row ends
        else if (continuation > 0 && chunks.Count > 1)
        {
            string first = Wrap(text, room, atSpaces: false)[0];

            chunks = [first, .. Wrap(text[first.Length..], room - continuation, atSpaces: false)];
        }

        var rows = new List<string>();
        int noteStart = entry.Note is null ? int.MaxValue : entry.Text.Length + 2;
        int consumed = 0;

        for (int index = 0; index < chunks.Count; index++)
        {
            string chunk = chunks[index];

            string head = index == 0
                ? $"[{LogQuiet}]{Markup.Escape(time)}[/]" + (mark.Length > 0 ? $"[{markColor}]{Markup.Escape(mark)}[/]" : string.Empty)
                : new string(' ', indent + continuation);

            rows.Add(head + Styled(chunk.TrimEnd('\n'), consumed));
            consumed += chunk.Length;
        }

        return rows;

        // The note is drawn faintly and the accents in their own style, wherever the wrapping has put them
        string Styled(string chunk, int offset)
        {
            var styled = new StringBuilder();
            int end = offset + chunk.Length;

            for (int at = offset; at < end;)
            {
                int next = entry.Accents
                    .SelectMany(accent => new[] { accent.Start, accent.Start + accent.Length })
                    .Append(noteStart)
                    .Append(end)
                    .Where(boundary => boundary > at)
                    .Min();

                string style = at >= noteStart
                    ? Theme.Dim
                    : entry.Accents.FirstOrDefault(accent => at >= accent.Start && at < accent.Start + accent.Length).Style ?? textStyle;

                styled.Append($"[{style}]{Markup.Escape(chunk[(at - offset)..(next - offset)])}[/]");
                at = next;
            }

            return styled.ToString();
        }
    }

    /// <summary>
    ///     The style a log level is named in: the more detailed a level, the fainter; warnings and failures
    ///     in the colors their lines have in the log.
    /// </summary>
    /// <param name="level">The level, as typed after /log.</param>
    public static string LevelStyle(string level) => level switch
    {
        "verbose" => Theme.Dim,
        "debug" => LogQuiet,
        "information" => ModelColor,
        "warning" => Theme.Warning,
        "error" => Theme.Failure,
        _ => $"bold {Theme.Failure}"
    };

    /// <summary>
    ///     The lines of the tree in front of a step: "├─ " for a step that has more after it,
    ///     "└─ " for the last one, set in further for a step of a step.
    /// </summary>
    private string Branch(ScreenEntry entry, ScreenEntry? next)
    {
        if (entry.Depth == 0)
            return string.Empty;

        bool isLast = next is null || next.Kind != EntryKind.Message || next.Depth < entry.Depth;

        string branch = IsAscii ? (isLast ? "`- " : "|- ") : isLast ? "└─ " : "├─ ";

        return new string(' ', 3 * (entry.Depth - 1)) + branch;
    }

    /// <summary>
    ///     "▸ " in front of the name of a group of steps.
    /// </summary>
    private string HeadingMark => IsAscii ? "* " : "\u25B8 ";

    private static string ToneStyle(MessageTone tone) => tone switch
    {
        MessageTone.Heading => $"bold {Theme.Accent}",
        MessageTone.Done => Theme.Success,
        MessageTone.Failed => Theme.Failure,
        MessageTone.Progress => LogQuiet,
        MessageTone.Key => $"bold {Theme.Success}",
        MessageTone.Library => LibraryText,
        MessageTone.Title => "bold #f0f6fc",
        _ => LogText
    };

    /// <summary>
    ///     The text the way a row can show it: a line break of any kind is one "\n", a tab is spaces,
    ///     other control characters are spaces, and line breaks at the end are gone.
    /// </summary>
    private static string Printable(string text)
    {
        var printable = new StringBuilder(text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    "));

        for (int index = 0; index < printable.Length; index++)
        {
            if (printable[index] != '\n' && char.IsControl(printable[index]))
                printable[index] = ' ';
        }

        return printable.ToString().TrimEnd('\n');
    }

    /// <summary>
    ///     Breaks a text of several lines into pieces: every line starts a row of its own and is wrapped
    ///     like <see cref="Wrap"/>. The last piece of a line keeps its "\n", so the pieces put together
    ///     still give the text back exactly.
    /// </summary>
    private static List<string> WrapLines(string text, int room)
    {
        string[] lines = text.Split('\n');
        var chunks = new List<string>();

        for (int index = 0; index < lines.Length; index++)
        {
            List<string> pieces = Wrap(lines[index], room);

            if (index < lines.Length - 1)
                pieces[^1] += "\n";

            chunks.AddRange(pieces);
        }

        return chunks;
    }

    /// <summary>
    ///     Breaks a text into pieces no wider than <paramref name="room"/> cells, between words where it can.
    ///     The pieces put together give the text back exactly.
    /// </summary>
    public static List<string> Wrap(string text, int room, bool atSpaces = true)
    {
        var chunks = new List<string>();
        int start = 0;

        while (start < text.Length)
        {
            int end = start;
            int used = 0;
            int lastSpace = -1;

            while (end < text.Length)
            {
                int length = char.IsSurrogatePair(text, end) ? 2 : 1;
                int cells = WidthOf(text.Substring(end, length));

                if (used + cells > room)
                    break;

                if (text[end] == ' ')
                    lastSpace = end;

                used += cells;
                end += length;
            }

            // A piece that has to be cut ends after the last space in it, unless the word fills the whole row
            if (atSpaces && end < text.Length && lastSpace > start)
                end = lastSpace + 1;

            if (end == start)
                end = start + (char.IsSurrogatePair(text, start) ? 2 : 1);

            chunks.Add(text[start..end]);
            start = end;
        }

        if (chunks.Count == 0)
            chunks.Add(string.Empty);

        return chunks;
    }

    private List<string> BuildInput(string input, int width)
    {
        (string topLeft, string topRight, string bottomLeft, string bottomRight, string horizontal, string vertical) =
            _theme == Theme.Full ? ("╭", "╮", "╰", "╯", "─", "│")
            : IsAscii ? ("+", "+", "+", "+", "-", "|")
            : ("┌", "┐", "└", "┘", "─", "│");

        int room = width - 2;

        // "│ > text█ │": the prompt, the text and the cursor share the inside of the box
        int textRoom = Math.Max(1, room - 4);

        string content;

        if (input.Length == 0)
        {
            // A translation longer than the box is cut, like any line that does not fit
            string placeholder = Wrap(Strings.ListenPlaceholder, textRoom)[0].TrimEnd();

            // The cursor stands where the typing will start, in front of the hint
            content = $"[{Theme.Highlight}]>[/] [{Theme.Highlight}]{_theme.BarFull}[/]{SetupUi.Dim(Markup.Escape(placeholder))}";
            content += new string(' ', Math.Max(0, room - 3 - 1 - WidthOf(placeholder)));
        }
        else
        {
            // A line longer than the box shows its end, where the typing happens
            string shown = input;

            while (WidthOf(shown) > textRoom)
                shown = shown[1..];

            content = $"[{Theme.Highlight}]>[/] {Markup.Escape(shown)}[{Theme.Highlight}]{_theme.BarFull}[/]";
            content += new string(' ', Math.Max(0, room - 3 - WidthOf(shown) - 1));
        }

        return
        [
            SetupUi.Dim(topLeft + Repeat(horizontal, room) + topRight),
            $"{SetupUi.Dim(vertical)} {content}{SetupUi.Dim(vertical)}",
            SetupUi.Dim(bottomLeft + Repeat(horizontal, room) + bottomRight),
        ];
    }

    /// <summary>
    ///     A row of the panel: its name in a column as wide as the widest name, then what it does.
    ///     The highlighted row stands out; the value in effect says so.
    /// </summary>
    private static string BuildPanelRow(IReadOnlyList<PanelRow> panel, int index, bool isSelected, int width)
    {
        PanelRow row = panel[index];

        int labelWidth = panel.Max(candidate => WidthOf(candidate.Label)) + 2;
        string label = $" {row.Label}{new string(' ', labelWidth - WidthOf(row.Label) - 1)}";

        string current = row.IsCurrent ? $"  {Strings.LevelCurrent}" : string.Empty;
        string description = row.Description;

        int room = width - 2 - labelWidth - 2 - WidthOf(current);

        if (WidthOf(description) > room)
            description = Wrap(description, Math.Max(1, room - 1))[0].TrimEnd() + "…";

        string shownLabel = isSelected
            ? $"[#ffffff on #3b2a52]{Markup.Escape(label)}[/]"
            : $"[{row.LabelStyle ?? Muted}]{Markup.Escape(label)}[/]";

        string shownDescription = $"[{(isSelected ? "#e6edf3" : Muted)}]{Markup.Escape(description)}[/]";
        string shownCurrent = current.Length > 0 ? $"[{Theme.Success}]{Markup.Escape(current)}[/]" : string.Empty;

        return $"  {shownLabel}  {shownDescription}{shownCurrent}";
    }

    private string BuildKeys(bool isPanelOpen, int width)
    {
        List<string> keys = isPanelOpen
            ? [$"Enter {Strings.ListenHintPick}", $"{_theme.Arrows} {Strings.HintSelect}", $"Esc {Strings.ListenHintClose}"]
            : [$"Enter {Strings.ListenHintRun}", $"{_theme.Arrows} PgUp PgDn {Strings.ListenHintLog}", $"Ctrl+C {Strings.ListenHintQuit}"];

        string line = "  " + string.Join(Separator, keys);

        // On a narrow terminal the keys that matter least go first
        while (WidthOf(line) > width && keys.Count > 1)
        {
            keys.RemoveAt(keys.Count - 2);
            line = "  " + string.Join(Separator, keys);
        }

        return $"[{Muted}]{Markup.Escape(line)}[/]";
    }

    private static string Repeat(string text, int count) =>
        count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(text, count));
}