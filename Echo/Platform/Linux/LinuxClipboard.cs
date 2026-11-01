namespace Echo.Platform.Linux;

/// <summary>
///     The two clipboards of Linux: the ordinary one (Ctrl+C) and the selection (what was last highlighted).
///     Programs paste with Shift+Insert from one or the other, so the text goes into both. Linux has no clipboard
///     of its own for programs without windows; small programs of the desktop hold the text: xsel or xclip,
///     or wl-copy (package wl-clipboard).
/// </summary>
/// <remarks>
///     On Wayland xsel and xclip come first as well: they work through the X11 layer that every Wayland desktop
///     runs for older programs, which hands the text on to the Wayland ones. wl-copy is the last resort there,
///     because on GNOME it opens a window for a moment to get the keyboard, and the text pasted right after could
///     go into that window.
/// </remarks>
/// <param name="display">The display server of the session.</param>
/// <param name="hasX11">Whether X11 programs can run: always on X11, on Wayland when its X11 layer is there.</param>
/// <param name="programs">Finds and runs those programs.</param>
[SupportedOSPlatform("linux")]
internal sealed class LinuxClipboard(DisplayServer display, bool hasX11, LinuxPrograms programs) : ITextClipboard
{
    private readonly DisplayServer _display = display;
    private readonly bool _hasX11 = hasX11;
    private readonly LinuxPrograms _programs = programs;

    /// <summary>
    ///     The programs that can fill the clipboard and the selection in this session, the first one found wins.
    /// </summary>
    internal static IReadOnlyList<(string Program, string Clipboard, string Selection)> Writers(DisplayServer display, bool hasX11)
    {
        var writers = new List<(string, string, string)>();

        if (hasX11)
            writers.AddRange([("xsel", "--clipboard --input", "--primary --input"), ("xclip", "-selection clipboard", "-selection primary")]);

        if (display == DisplayServer.Wayland)
            writers.Add(("wl-copy", string.Empty, "--primary"));

        return writers;
    }

    /// <inheritdoc/>
    public async Task SetTextAsync(string text)
    {
        (string Program, string Clipboard, string Selection) writer = Writers(_display, _hasX11).FirstOrDefault(candidate => _programs.Exists(candidate.Program));

        if (writer.Program is null)
        {
            string needed = string.Join(" / ", Writers(_display, _hasX11).Select(candidate => candidate.Program).DefaultIfEmpty("xsel"));

            throw new IOException($"Couldn't put the text into the clipboard: no {needed} on this system.");
        }

        await _programs.FeedAsync(writer.Program, writer.Clipboard, text);
        await _programs.FeedAsync(writer.Program, writer.Selection, text);
    }
}