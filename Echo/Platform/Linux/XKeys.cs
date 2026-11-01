namespace Echo.Platform.Linux;

/// <summary>
///     The keys of the settings as Linux names them: by their "keysym", a number with a name, such as 0x60 "grave"
///     for the key left of 1. X11 grabs a key by this number; the desktop portal of Wayland takes the name.
/// </summary>
internal static class XKeys
{
    /// <summary>
    ///     The modifier bits X11 puts into the state of a key event: Shift, Ctrl, Alt and the Windows key (Super).
    /// </summary>
    public const uint ShiftMask = 1;
    public const uint ControlMask = 4;
    public const uint Mod1Mask = 8;
    public const uint Mod4Mask = 64;

    private static readonly Dictionary<KeyCode, (uint Keysym, string Name)> _keys = Build();

    /// <summary>
    ///     The keysym of a key, or null for a key Linux has no fixed name for.
    /// </summary>
    public static (uint Keysym, string Name)? Find(KeyCode key) =>
        _keys.TryGetValue(key, out (uint Keysym, string Name) found) ? found : null;

    /// <summary>
    ///     The key a keysym name stands for, such as "grave" for the key left of 1; null for a name Echo does not know.
    ///     Letters are matched whatever their case: Linux names "a" and "A" the same key with and without Shift.
    /// </summary>
    public static KeyCode? FindByName(string name)
    {
        foreach ((KeyCode key, (uint _, string known)) in _keys)
        {
            if (string.Equals(known, name, known.Length == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return key;
        }

        return null;
    }

    /// <summary>
    ///     The modifier bit of a 'Modifier' setting, and its name in a shortcut of the desktop portal. X11 does not
    ///     tell the left and the right Ctrl apart in a shortcut, so either one does.
    /// </summary>
    public static (uint Mask, string Name)? FindModifier(string? setting) =>
        (setting ?? string.Empty).Replace(" ", string.Empty).ToLowerInvariant() switch
        {
            "" => null,
            "ctrl" or "leftctrl" or "rightctrl" => (ControlMask, "CTRL"),
            "shift" or "leftshift" or "rightshift" => (ShiftMask, "SHIFT"),
            "alt" or "leftalt" or "rightalt" => (Mod1Mask, "ALT"),
            "win" or "meta" or "leftmeta" or "rightmeta" => (Mod4Mask, "LOGO"),
            _ => null
        };

    private static Dictionary<KeyCode, (uint Keysym, string Name)> Build()
    {
        var keys = new Dictionary<KeyCode, (uint Keysym, string Name)>
        {
            [KeyCode.VcBackQuote] = (0x60, "grave"),
            [KeyCode.VcMinus] = (0x2d, "minus"),
            [KeyCode.VcEquals] = (0x3d, "equal"),
            [KeyCode.VcOpenBracket] = (0x5b, "bracketleft"),
            [KeyCode.VcCloseBracket] = (0x5d, "bracketright"),
            [KeyCode.VcBackslash] = (0x5c, "backslash"),
            [KeyCode.VcSemicolon] = (0x3b, "semicolon"),
            [KeyCode.VcQuote] = (0x27, "apostrophe"),
            [KeyCode.VcComma] = (0x2c, "comma"),
            [KeyCode.VcPeriod] = (0x2e, "period"),
            [KeyCode.VcSlash] = (0x2f, "slash"),
            [KeyCode.VcSpace] = (0x20, "space"),
            [KeyCode.VcEnter] = (0xff0d, "Return"),
            [KeyCode.VcEscape] = (0xff1b, "Escape"),
            [KeyCode.VcTab] = (0xff09, "Tab"),
            [KeyCode.VcBackspace] = (0xff08, "BackSpace"),
            [KeyCode.VcCapsLock] = (0xffe5, "Caps_Lock"),
            [KeyCode.VcPrintScreen] = (0xff61, "Print"),
            [KeyCode.VcScrollLock] = (0xff14, "Scroll_Lock"),
            [KeyCode.VcPause] = (0xff13, "Pause"),
            [KeyCode.VcInsert] = (0xff63, "Insert"),
            [KeyCode.VcDelete] = (0xffff, "Delete"),
            [KeyCode.VcHome] = (0xff50, "Home"),
            [KeyCode.VcEnd] = (0xff57, "End"),
            [KeyCode.VcPageUp] = (0xff55, "Prior"),
            [KeyCode.VcPageDown] = (0xff56, "Next"),
            [KeyCode.VcLeft] = (0xff51, "Left"),
            [KeyCode.VcUp] = (0xff52, "Up"),
            [KeyCode.VcRight] = (0xff53, "Right"),
            [KeyCode.VcDown] = (0xff54, "Down"),
            [KeyCode.VcNumLock] = (0xff7f, "Num_Lock"),
            [KeyCode.VcNumPadDivide] = (0xffaf, "KP_Divide"),
            [KeyCode.VcNumPadMultiply] = (0xffaa, "KP_Multiply"),
            [KeyCode.VcNumPadSubtract] = (0xffad, "KP_Subtract"),
            [KeyCode.VcNumPadAdd] = (0xffab, "KP_Add"),
            [KeyCode.VcNumPadEnter] = (0xff8d, "KP_Enter"),
            [KeyCode.VcNumPadDecimal] = (0xffae, "KP_Decimal"),
            [KeyCode.VcContextMenu] = (0xff67, "Menu"),
            [KeyCode.VcLeftShift] = (0xffe1, "Shift_L"),
            [KeyCode.VcRightShift] = (0xffe2, "Shift_R"),
            [KeyCode.VcLeftControl] = (0xffe3, "Control_L"),
            [KeyCode.VcRightControl] = (0xffe4, "Control_R"),
            [KeyCode.VcLeftAlt] = (0xffe9, "Alt_L"),
            [KeyCode.VcRightAlt] = (0xffea, "Alt_R"),
            [KeyCode.VcLeftMeta] = (0xffeb, "Super_L"),
            [KeyCode.VcRightMeta] = (0xffec, "Super_R"),
        };

        for (char letter = 'A'; letter <= 'Z'; letter++)
            keys[Enum.Parse<KeyCode>($"Vc{letter}")] = (char.ToLowerInvariant(letter), char.ToLowerInvariant(letter).ToString());

        for (int digit = 0; digit <= 9; digit++)
        {
            keys[Enum.Parse<KeyCode>($"Vc{digit}")] = ((uint)('0' + digit), digit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            keys[Enum.Parse<KeyCode>($"VcNumPad{digit}")] = ((uint)(0xffb0 + digit), $"KP_{digit}");
        }

        for (int number = 1; number <= 24; number++)
            keys[Enum.Parse<KeyCode>($"VcF{number}")] = ((uint)(0xffbe + number - 1), $"F{number}");

        return keys;
    }
}