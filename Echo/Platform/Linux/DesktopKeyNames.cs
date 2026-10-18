using Echo.Setup.Hotkeys;

namespace Echo.Platform.Linux;

/// <summary>
///     Turns the desktop's description of Echo's key into the name Echo shows everywhere else. GNOME describes the
///     key in its own words around the Linux name of the key ("Нажмите grave", "Press CTRL+grave"); Echo shows the
///     key the way it does on Windows ("`", "Ctrl + `").
/// </summary>
internal static class DesktopKeyNames
{
    private const char Plus = '+';

    /// <summary>
    ///     The key named the way Echo names keys; the description as it is when its last word is not a key.
    /// </summary>
    public static string Readable(string description)
    {
        string trigger = description.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        string[] parts = trigger.Split(Plus);

        if (parts.Length == 0 || parts[^1].Length == 0)
            return description;

        List<string> names = [];

        foreach (string modifier in parts[..^1])
        {
            if (Modifier(modifier) is not { } name)
                return description;

            names.Add(name);
        }

        if (Key(parts[^1]) is not { } key)
            return description;

        names.Add(key);

        return string.Join($" {Plus} ", names);
    }

    /// <summary>
    ///     The name of a modifier, whichever way the desktop writes it: "CTRL", "Control", "<Ctrl>".
    /// </summary>
    private static string? Modifier(string text) =>
        text.Trim('<', '>').ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" or "PRIMARY" => "Ctrl",
            "ALT" or "MOD1" => "Alt",
            "SHIFT" => "Shift",
            "LOGO" or "SUPER" or "META" or "MOD4" => "Super",
            "NUM" => "Num Lock",
            _ => null
        };

    /// <summary>
    ///     The name of a key: by its Linux name, such as "grave" or "F8", or the character itself.
    /// </summary>
    private static string? Key(string text)
    {
        if (XKeys.FindByName(text) is { } key)
            return HotkeyNames.Display(new HotkeyChoice(key.ToString(), string.Empty));

        // A character the desktop already wrote as itself, such as "`"
        return text.Length == 1 ? text.ToUpperInvariant() : null;
    }
}