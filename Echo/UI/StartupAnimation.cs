namespace Echo.UI;

/// <summary>
///     Builds the frames of the startup logo animation: 'echo' is typed letter by letter, the last letter
///     echoes twice and fades out while it turns into an ear, then the highlight drips down the logo
///     into an equalizer that settles into a line.
/// </summary>
internal static class StartupAnimation
{
    /// <summary>
    ///     One logo position: a letter or the ear.
    /// </summary>
    /// <param name="Glyph">The glyph key, or <see cref="NoGlyph"/> when the position is empty.</param>
    /// <param name="Intensity">Brightness: 0 is hidden, 1 is the logo color, above 1 is highlighted.</param>
    /// <param name="Dissolve">How far the glyph has crumbled: 0 is intact, 3 is almost gone.</param>
    internal readonly record struct Slot(char Glyph, float Intensity = 1f, int Dissolve = 0);

    /// <summary>
    ///     A single animation frame.
    /// </summary>
    /// <param name="Slots">The logo positions, left to right.</param>
    /// <param name="DelayMs">How long the frame stays on the screen before the next one is due.</param>
    /// <param name="Highlights">Logo cells that are lit by a falling drop.</param>
    /// <param name="Bars">
    ///     Equalizer bar heights under the logo, one per column: -1 is no bar yet, 0 is the resting line.
    ///     Null when the equalizer is not shown.
    /// </param>
    internal sealed record Frame(
        Slot[] Slots,
        int DelayMs,
        IReadOnlyList<(int Row, int Column, float Intensity)> Highlights,
        int[]? Bars);

    internal const int GlyphHeight = 6;

    /// <summary>
    ///     Rows of a rendered frame: the logo and the equalizer under it.
    /// </summary>
    internal const int RowsCount = GlyphHeight + 1;

    internal const char NoGlyph = '\0';
    internal const char EarGlyph = '@';

    private const int SlotsCount = 6;
    private const int LogoWidth = 33;
    private const int FrameWidth = 51;

    // ------------------------------------------------------------------------------------------------
    // Timing. Every value is in milliseconds and can be changed on its own: the phases do not share
    // a clock, so slowing one of them down does not slow the others.
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    ///     How long each of the first three letters stays the newest one before the next letter appears:
    ///     E, C, H.
    /// </summary>
    private static readonly int[] _letterDelaysMs = [50, 50, 50];

    /// <summary>
    ///     The same for the three O's: the O of the word, its first echo, its second echo.
    ///     The last value is the pause between the second echo and the moment the O starts to crumble.
    /// </summary>
    private static readonly int[] _oDelaysMs = [50, 50, 50];

    /// <summary>
    ///     One step of the last O crumbling into dust; there are <see cref="CrumbleSteps"/> of them.
    ///     The echoes fade out on the same steps.
    /// </summary>
    private const int CrumbleStepMs = 50;

    /// <summary>
    ///     One step of the ear assembling from the dust; there are <see cref="AssembleSteps"/> of them.
    /// </summary>
    private const int AssembleStepMs = 50;

    /// <summary>
    ///     How long a drop of the rain takes to fall one row. The rain starts when the ear starts to assemble.
    /// </summary>
    private const int RainStepMs = 80;

    /// <summary>
    ///     One step of an equalizer bar bouncing after a drop has landed on it; the heights of the steps
    ///     are in <see cref="_barBounce"/>.
    /// </summary>
    private const int EqualizerStepMs = 50;

    // ------------------------------------------------------------------------------------------------
    // Shape of the rain, counted in steps of the rain rather than in milliseconds.
    // ------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Columns start dropping at a random moment within this many steps, so the rain is uneven.
    /// </summary>
    private const int RainSpreadSteps = 5;

    /// <summary>
    ///     A column that drops twice lets its second drop go this many steps after the first.
    /// </summary>
    private const int SecondDropGapSteps = 7;

    /// <summary>
    ///     The share of columns that drop twice.
    /// </summary>
    private const double SecondDropChance = 0.55;

    private const int CrumbleSteps = 3;
    private const int AssembleSteps = 3;

    private const float HighlightIntensity = 1.3f;
    private const float TrailIntensity = 1.12f;
    private const float RestingBarIntensity = 0.45f;

    private const int EqualizerColorRow = 0;

    private const string LeftMargin = "  ";
    private const string BoxCharacters = "╗╔═║╚╝";

    /// <summary>
    ///     The logo color of every row, top to bottom: a violet to pink gradient.
    /// </summary>
    private static readonly (byte R, byte G, byte B)[] _rowColors =
    [
        (180, 140, 255),
        (192, 132, 252),
        (215, 126, 230),
        (236, 120, 208),
        (255, 122, 198),
        (255, 122, 198),
    ];

    private static readonly Dictionary<char, string[]> _glyphs = new()
    {
        ['e'] = ["███████╗", "██╔════╝", "█████╗  ", "██╔══╝  ", "███████╗", "╚══════╝"],
        ['c'] = [" ██████╗", "██╔════╝", "██║     ", "██║     ", "╚██████╗", " ╚═════╝"],
        ['h'] = ["██╗  ██╗", "██║  ██║", "███████║", "██╔══██║", "██║  ██║", "╚═╝  ╚═╝"],
        ['o'] = [" ██████╗ ", "██╔═══██╗", "██║   ██║", "██║   ██║", "╚██████╔╝", " ╚═════╝ "],
        [EarGlyph] = [" ▄█████▄ ", "██▀   ▀██", "██ ▄▀▀ ██", "▀█▄▀▀ ▄█▀", " ▀█▄▄█▀  ", "  ▀▀▀    "],
    };

    /// <summary>
    ///     How high a bar jumps on every frame after a drop has landed on it.
    /// </summary>
    private static readonly int[] _barBounce = [4, 5, 7, 6, 4, 3, 2, 1];

    /// <summary>
    ///     How bright the two echoes are: before the O starts to crumble, then on every step of the
    ///     crumbling and assembling. Past the end of the table they are gone.
    /// </summary>
    private static readonly (float First, float Second)[] _echoFade =
    [
        (0.6f, 0.3f),
        (0.3f, 0.2f),
        (0.3f, 0.1f),
        (0.15f, 0f),
    ];

    /// <summary>
    ///     Builds the whole animation: one frame for every moment at which the picture changes.
    ///     The last frame is the resting logo.
    /// </summary>
    internal static IReadOnlyList<Frame> BuildFrames()
    {
        Timeline timeline = Timeline.Create();

        List<int> moments = [.. timeline.Moments()];

        return [.. moments.Select((moment, index) => timeline.FrameAt(
            moment,
            durationMs: index + 1 < moments.Count ? moments[index + 1] - moment : 0))];
    }

    /// <summary>
    ///     When everything in the animation happens, counted in milliseconds from its start.
    /// </summary>
    /// <param name="LetterTimes">When each of the six letters of 'echooo' appears.</param>
    /// <param name="CrumbleStart">When the last O starts to crumble.</param>
    /// <param name="AssembleStart">When the ear starts to assemble; the rain starts at the same moment.</param>
    /// <param name="EarReady">When the ear is complete.</param>
    /// <param name="DropStarts">For every column, when each of its drops starts to fall.</param>
    /// <param name="Amplitudes">For every column, how high its equalizer bar jumps.</param>
    private sealed record Timeline(
        int[] LetterTimes,
        int CrumbleStart,
        int AssembleStart,
        int EarReady,
        int[][] DropStarts,
        float[] Amplitudes)
    {
        private const string Letters = "echooo";

        /// <summary>
        ///     How long a drop falls before it reaches the equalizer.
        /// </summary>
        private const int FallMs = GlyphHeight * RainStepMs;

        public static Timeline Create()
        {
            int[] gaps = [.. _letterDelaysMs, .. _oDelaysMs];

            var letterTimes = new int[Letters.Length];
            int time = 0;

            for (int i = 0; i < letterTimes.Length; i++)
            {
                letterTimes[i] = time;
                time += gaps[i];
            }

            int assembleStart = time + CrumbleSteps * CrumbleStepMs;

            (int[][] delays, float[] amplitudes) = BuildRain();

            return new Timeline(
                letterTimes,
                CrumbleStart: time,
                assembleStart,
                EarReady: assembleStart + AssembleSteps * AssembleStepMs,
                [.. delays.Select(column => column.Select(delay => assembleStart + delay * RainStepMs).ToArray())],
                amplitudes);
        }

        /// <summary>
        ///     Every moment at which something on the screen changes, in order.
        /// </summary>
        public SortedSet<int> Moments()
        {
            var moments = new SortedSet<int>(LetterTimes);

            for (int step = 0; step < CrumbleSteps; step++)
                moments.Add(CrumbleStart + step * CrumbleStepMs);

            for (int step = 0; step <= AssembleSteps; step++)
                moments.Add(AssembleStart + step * AssembleStepMs);

            foreach (int dropStart in DropStarts.SelectMany(column => column))
            {
                for (int row = 0; row <= GlyphHeight; row++)
                    moments.Add(dropStart + row * RainStepMs);

                for (int step = 0; step <= _barBounce.Length; step++)
                    moments.Add(dropStart + FallMs + step * EqualizerStepMs);
            }

            return moments;
        }

        /// <summary>
        ///     What the screen shows at the given moment.
        /// </summary>
        public Frame FrameAt(int time, int durationMs)
        {
            var slots = new Slot[SlotsCount];
            Array.Fill(slots, new Slot(NoGlyph, 0f));

            // Every letter of the word lights up for as long as it is the newest one
            for (int i = 0; i < 4 && time >= LetterTimes[i]; i++)
                slots[i] = new Slot(Letters[i], time < LetterTimes[i + 1] ? HighlightIntensity : 1f);

            if (time >= EarReady)
                slots[3] = new Slot(EarGlyph);
            else if (time >= AssembleStart)
                slots[3] = new Slot(EarGlyph, 1f, AssembleSteps - (time - AssembleStart) / AssembleStepMs);
            else if (time >= CrumbleStart)
                slots[3] = new Slot(Letters[3], 1f, 1 + (time - CrumbleStart) / CrumbleStepMs);

            // The echoes fade out while the last letter crumbles and the ear takes its place
            int fadeStep = time < CrumbleStart
                ? 0
                : time < AssembleStart
                    ? 1 + (time - CrumbleStart) / CrumbleStepMs
                    : 1 + CrumbleSteps + (time - AssembleStart) / AssembleStepMs;

            if (fadeStep < _echoFade.Length)
            {
                if (time >= LetterTimes[4])
                    slots[4] = new Slot(Letters[4], _echoFade[fadeStep].First);

                if (time >= LetterTimes[5])
                    slots[5] = new Slot(Letters[5], _echoFade[fadeStep].Second);
            }

            if (time < AssembleStart)
                return new Frame(slots, durationMs, Highlights: [], Bars: null);

            // Every column starts at its own moment: a drop runs down the logo, and when it lands
            // it kicks up the equalizer bar of that column, which then settles into the line
            var highlights = new List<(int Row, int Column, float Intensity)>();
            var bars = new int[LogoWidth];

            for (int column = 0; column < LogoWidth; column++)
            {
                int height = -1;

                foreach (int dropStart in DropStarts[column])
                {
                    if (time < dropStart)
                        continue;

                    int row = (time - dropStart) / RainStepMs;

                    if (row < GlyphHeight)
                        highlights.Add((row, column, HighlightIntensity));

                    if (row >= 1 && row <= GlyphHeight)
                        highlights.Add((row - 1, column, TrailIntensity));

                    if (time >= dropStart + FallMs)
                    {
                        int step = (time - dropStart - FallMs) / EqualizerStepMs;
                        int bounce = step < _barBounce.Length
                            ? (int)Math.Round(_barBounce[step] * Amplitudes[column], MidpointRounding.AwayFromZero)
                            : 0;

                        height = Math.Max(height, bounce);
                    }
                }

                bars[column] = height;
            }

            return new Frame(slots, durationMs, highlights, bars);
        }
    }

    /// <summary>
    ///     Renders a frame into console rows: the logo and the equalizer row under it.
    /// </summary>
    /// <param name="frame">The frame to render.</param>
    /// <param name="useMarkup">
    ///     Whether to color the cells with Spectre.Console markup; otherwise plain text is returned.
    /// </param>
    /// <param name="barCharacters">Eight symbols of growing height the equalizer is drawn with.</param>
    internal static string[] RenderRows(Frame frame, bool useMarkup, string barCharacters)
    {
        var characters = new char[GlyphHeight, FrameWidth];
        var intensities = new float[GlyphHeight, FrameWidth];

        int column = 0;
        foreach (Slot slot in frame.Slots)
        {
            int width = SlotWidth(column);

            if (slot.Glyph != NoGlyph && slot.Intensity > 0f)
            {
                for (int row = 0; row < GlyphHeight; row++)
                {
                    string glyphRow = _glyphs[slot.Glyph][row];

                    for (int i = 0; i < glyphRow.Length; i++)
                    {
                        characters[row, column + i] = Dissolve(glyphRow[i], slot.Dissolve);
                        intensities[row, column + i] = slot.Intensity;
                    }
                }
            }

            column += width;
        }

        // A drop only lights what is already drawn
        foreach ((int row, int highlightedColumn, float intensity) in frame.Highlights)
        {
            if (IsDrawn(characters[row, highlightedColumn]))
            {
                intensities[row, highlightedColumn] = Math.Max(intensities[row, highlightedColumn], intensity);
            }
        }

        var rows = new string[RowsCount];

        for (int row = 0; row < GlyphHeight; row++)
        {
            int currentRow = row;

            rows[row] = RenderRow(
                i => IsDrawn(characters[currentRow, i]) ? characters[currentRow, i] : ' ',
                i => ToMarkupColor(_rowColors[currentRow], intensities[currentRow, i]),
                useMarkup);
        }

        rows[GlyphHeight] = RenderRow(
            i => frame.Bars is null || i >= LogoWidth || frame.Bars[i] < 0
                ? ' '
                : barCharacters[Math.Min(barCharacters.Length - 1, frame.Bars[i])],
            i => ToMarkupColor(_rowColors[EqualizerColorRow], frame.Bars![i] == 0 ? RestingBarIntensity : 1f),
            useMarkup);

        return rows;
    }

    /// <summary>
    ///     Picks when every column drops and how high its bar jumps. Some columns drop twice,
    ///     so the equalizer bounces a second time. The choice is fixed, the animation never changes.
    /// </summary>
    private static (int[][] Delays, float[] Amplitudes) BuildRain()
    {
        Func<double> nextDelay = CreateSequence(seed: 23);
        var delays = new int[LogoWidth][];

        for (int column = 0; column < LogoWidth; column++)
        {
            int first = (int)Math.Floor(nextDelay() * RainSpreadSteps);

            delays[column] = nextDelay() < SecondDropChance ? [first, first + SecondDropGapSteps] : [first];
        }

        Func<double> nextAmplitude = CreateSequence(seed: 21);
        var amplitudes = new float[LogoWidth];

        for (int column = 0; column < LogoWidth; column++)
            amplitudes[column] = (float)(0.45 + nextAmplitude() * 0.55);

        return (delays, amplitudes);
    }

    /// <summary>
    ///     A repeatable sequence of numbers between 0 and 1.
    /// </summary>
    private static Func<double> CreateSequence(long seed)
    {
        const long Modulus = 2147483647;
        long state = seed;

        return () =>
        {
            state = state * 16807 % Modulus;

            return (double)state / Modulus;
        };
    }

    /// <summary>
    ///     The first three letters are narrower than the last position and its two echoes.
    /// </summary>
    private static int SlotWidth(int column) =>
        column < 24 ? 8 : 9;

    private static bool IsDrawn(char character) =>
        character != NoGlyph && character != ' ';

    private static char Dissolve(char character, int level)
    {
        if (level == 0 || character == ' ')
            return character;

        if (BoxCharacters.Contains(character))
            return level switch { 1 => character, 2 => '░', _ => ' ' };

        return level switch { 1 => '▓', 2 => '▒', _ => '░' };
    }

    private static string RenderRow(Func<int, char> characterAt, Func<int, string> colorAt, bool useMarkup)
    {
        var sb = new StringBuilder(LeftMargin);
        string? currentColor = null;

        for (int i = 0; i < FrameWidth; i++)
        {
            char character = characterAt(i);

            if (useMarkup && character != ' ')
            {
                string color = colorAt(i);

                if (color != currentColor)
                {
                    if (currentColor is not null)
                        sb.Append("[/]");

                    sb.Append('[').Append(color).Append(']');
                    currentColor = color;
                }
            }

            sb.Append(character);
        }

        if (currentColor is not null)
            sb.Append("[/]");

        return sb.ToString();
    }

    private static string ToMarkupColor((byte R, byte G, byte B) color, float intensity)
    {
        return
            $"rgb({Blend(color.R)},{Blend(color.G)},{Blend(color.B)})";

        // Below 1 the color fades out, above 1 it is mixed with white
        int Blend(byte channel) => intensity <= 1f
            ? (int)Math.Round(channel * intensity)
            : (int)Math.Round(channel + (byte.MaxValue - channel) * Math.Min(1f, (intensity - 1f) / (HighlightIntensity - 1f)));
    }
}