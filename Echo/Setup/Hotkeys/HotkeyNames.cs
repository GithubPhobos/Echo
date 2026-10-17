using System.Text.RegularExpressions;
using SharpHook.Data;

namespace Echo.Setup.Hotkeys;

/// <summary>
///     Converts between keyboard hook values, setting values and what is shown to the user.
/// </summary>
internal static partial class HotkeyNames
{
    /// <summary>
    ///     Modifier keys with the mask bit they set and the name the 'Modifier' setting understands.
    ///     The side is always kept: a mask for "either side" would only match both sides held together.
    /// </summary>
    private static readonly (KeyCode Key, EventMask Mask, string Setting, string Display, string Short)[] _modifiers =
    [
        (KeyCode.VcLeftControl, EventMask.LeftCtrl, "leftctrl", "Left Ctrl", "LCtrl"),
        (KeyCode.VcRightControl, EventMask.RightCtrl, "rightctrl", "Right Ctrl", "RCtrl"),
        (KeyCode.VcLeftAlt, EventMask.LeftAlt, "leftalt", "Left Alt", "LAlt"),
        (KeyCode.VcRightAlt, EventMask.RightAlt, "rightalt", "Right Alt", "RAlt"),
        (KeyCode.VcLeftShift, EventMask.LeftShift, "leftshift", "Left Shift", "LShift"),
        (KeyCode.VcRightShift, EventMask.RightShift, "rightshift", "Right Shift", "RShift"),
        (KeyCode.VcLeftMeta, EventMask.LeftMeta, "leftmeta", "Left Win", "LWin"),
        (KeyCode.VcRightMeta, EventMask.RightMeta, "rightmeta", "Right Win", "RWin"),
    ];

    private static readonly Dictionary<KeyCode, string> _symbols = new()
    {
        [KeyCode.VcBackQuote] = "`",
        [KeyCode.VcMinus] = "-",
        [KeyCode.VcEquals] = "=",
        [KeyCode.VcOpenBracket] = "[",
        [KeyCode.VcCloseBracket] = "]",
        [KeyCode.VcBackslash] = "\\",
        [KeyCode.VcSlash] = "/",
        [KeyCode.VcSemicolon] = ";",
        [KeyCode.VcQuote] = "'",
        [KeyCode.VcComma] = ",",
        [KeyCode.VcPeriod] = ".",
    };

    /// <summary>
    ///     Builds the setting values for a key pressed while <paramref name="mask"/> modifiers were held.
    /// </summary>
    public static HotkeyChoice ToChoice(KeyCode key, EventMask mask)
    {
        // A modifier pressed on its own is the key itself, not "itself plus itself"
        if (_modifiers.Any(modifier => modifier.Key == key))
            return new HotkeyChoice(key.ToString(), string.Empty);

        string modifier = _modifiers.FirstOrDefault(candidate => mask.HasFlag(candidate.Mask)).Setting ?? string.Empty;

        return new HotkeyChoice(key.ToString(), modifier);
    }

    /// <summary>
    ///     A name a person recognizes, e.g. "Left Ctrl + F8" or "`".
    /// </summary>
    /// <param name="choice">The key and its modifier.</param>
    /// <param name="isShort">Names the side of a modifier with one letter, e.g. "RCtrl + F8", where room is short.</param>
    public static string Display(HotkeyChoice choice, bool isShort = false)
    {
        string key = DisplayKey(choice.Key, isShort);

        var modifier = _modifiers.FirstOrDefault(candidate => candidate.Setting == choice.Modifier);

        if (modifier.Display is null)
            return key;

        return $"{(isShort ? modifier.Short : modifier.Display)} + {key}";
    }

    private static string DisplayKey(string name, bool isShort)
    {
        if (!Enum.TryParse(name, ignoreCase: true, out KeyCode key))
            return name;

        if (_symbols.TryGetValue(key, out string? symbol))
            return symbol;

        var modifier = _modifiers.FirstOrDefault(candidate => candidate.Key == key);
        if (modifier.Display is not null)
            return isShort ? modifier.Short : modifier.Display;

        // "VcNumPad0" reads better as "Num Pad 0"
        string bare = name.StartsWith("Vc", StringComparison.Ordinal) ? name[2..] : name;

        return WordBoundary().Replace(bare, " ");
    }

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z0-9])")]
    private static partial Regex WordBoundary();
}