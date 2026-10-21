using System.Runtime.InteropServices;
using Spectre.Console;

namespace Echo.UI;

/// <summary>
///     The look of the interface: colors and the symbols a particular terminal can actually draw.
/// </summary>
/// <param name="Pointer">Marks the highlighted row of a list.</param>
/// <param name="RadioOn">Marks the selected choice.</param>
/// <param name="RadioOff">Marks the other choices.</param>
/// <param name="BarFull">The filled part of a progress bar.</param>
/// <param name="BarEmpty">The remaining part of a progress bar.</param>
/// <param name="Good">Marks success.</param>
/// <param name="Bad">Marks failure.</param>
/// <param name="Record">Marks an ongoing recording.</param>
/// <param name="StepSeparator">Stands between the steps in the step line.</param>
/// <param name="Dot">Separates short pieces of text on one line.</param>
/// <param name="Arrows">The up and down keys in the hints.</param>
/// <param name="LevelBars">Eight symbols of growing height, for meters and the equalizer.</param>
internal sealed record Theme(
    string Pointer,
    string RadioOn,
    string RadioOff,
    string BarFull,
    string BarEmpty,
    string Good,
    string Bad,
    string Record,
    string StepSeparator,
    string Dot,
    string Arrows,
    string LevelBars)
{
    public const string Accent = "#c084fc";
    public const string Highlight = "#ff7ac6";
    public const string Dim = "#6e7681";
    public const string Success = "#7ee787";
    public const string Failure = "#ff7b72";
    public const string Warning = "#ff9e64";

    /// <summary>
    ///     For terminals that can fall back to another font when a symbol is missing.
    /// </summary>
    public static Theme Full { get; } = new("►", "◉", "○", "█", "░", "√", "×", "●", "›", "·", "↑↓", "▁▂▃▄▅▆▇█");

    /// <summary>
    ///     For the classic Windows console: only symbols present in its standard fonts.
    /// </summary>
    public static Theme Safe { get; } = Full with { RadioOn = "●", LevelBars = "_▄▄▄▄███" };

    /// <summary>
    ///     For terminals without Unicode.
    /// </summary>
    public static Theme Ascii { get; } = new(">", "(*)", "( )", "#", "-", "+", "x", "*", ">", "-", "Up/Down", "_..::|||");

    /// <summary>
    ///     The mark of work under way: dots going round, a turn every second, where the font has them;
    ///     the mark of a recording elsewhere.
    /// </summary>
    /// <param name="clock">How long the work has been going on.</param>
    public string Spinner(TimeSpan clock)
    {
        if (this != Full)
            return Record;

        const string frames = "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";

        return frames[(int)(clock.TotalMilliseconds / 100) % frames.Length].ToString();
    }

    /// <summary>
    ///     Picks the richest set of symbols the terminal is known to draw.
    /// </summary>
    public static Theme Detect(IAnsiConsole console)
    {
        if (!console.Profile.Capabilities.Unicode)
            return Ascii;

        // Modern terminals mostly announce themselves; the classic Windows console does not and has no font
        // fallback. A terminal that Windows opened for a program started elsewhere, such as from Visual Studio,
        // does not announce itself, but its stand-in console window gives it away
        bool isModernTerminal =
            !RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_SESSION"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERM_PROGRAM"))
            || Platform.Windows.WindowsTerminalFocus.IsHostedByTerminal();

        return isModernTerminal ? Full : Safe;
    }
}