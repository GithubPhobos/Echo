using SharpHook.Data;

namespace Echo.Services;

/// <summary>
///     Provides utility methods to map human-readable string representations of keyboard keys 
///     and modifiers from the configuration to <see cref="SharpHook"/> specific enums.
/// </summary>
internal static class KeyMapperService
{
    private static readonly Dictionary<string, KeyCode> SymbolMap = new()
    {
        { "`", KeyCode.VcBackQuote },
        { "~", KeyCode.VcBackQuote },
        { "tilde", KeyCode.VcBackQuote },
        { "backquote", KeyCode.VcBackQuote },

        { "-", KeyCode.VcMinus },
        { "_", KeyCode.VcMinus },
        { "minus", KeyCode.VcMinus },
        { "=", KeyCode.VcEquals },
        { "+", KeyCode.VcEquals },

        { "[", KeyCode.VcOpenBracket },
        { "{", KeyCode.VcOpenBracket },
        { "]", KeyCode.VcCloseBracket },
        { "}", KeyCode.VcCloseBracket },

        { "\\", KeyCode.VcBackslash },
        { "|", KeyCode.VcBackslash },
        { "/", KeyCode.VcSlash },
        { "?", KeyCode.VcSlash },

        { ";", KeyCode.VcSemicolon },
        { ":", KeyCode.VcSemicolon },
        { "'", KeyCode.VcQuote },
        { "\"", KeyCode.VcQuote },
        { ",", KeyCode.VcComma },
        { "<", KeyCode.VcComma },
        { ".", KeyCode.VcPeriod },
        { ">", KeyCode.VcPeriod },

        { "space", KeyCode.VcSpace },
        { " ", KeyCode.VcSpace },
        { "enter", KeyCode.VcEnter },
        { "esc", KeyCode.VcEscape },
        { "escape", KeyCode.VcEscape },
        { "tab", KeyCode.VcTab },
        { "backspace", KeyCode.VcBackspace },
        { "capslock", KeyCode.VcCapsLock },

        { "ctrl", KeyCode.VcLeftControl },
        { "leftctrl", KeyCode.VcLeftControl },
        { "rightctrl", KeyCode.VcRightControl },
        { "alt", KeyCode.VcLeftAlt },
        { "leftalt", KeyCode.VcLeftAlt },
        { "rightalt", KeyCode.VcRightAlt },
        { "shift", KeyCode.VcLeftShift },
        { "leftshift", KeyCode.VcLeftShift },
        { "rightshift", KeyCode.VcRightShift },
        { "win", KeyCode.VcLeftMeta },
        { "meta", KeyCode.VcLeftMeta },
    };

    private static readonly Dictionary<string, EventMask> ModifierMap = new()
    {
        { "alt", EventMask.Alt },
        { "leftalt", EventMask.LeftAlt },
        { "rightalt", EventMask.RightAlt },

        { "ctrl", EventMask.Ctrl },
        { "leftctrl", EventMask.LeftCtrl },
        { "rightctrl", EventMask.RightCtrl },

        { "shift", EventMask.Shift },
        { "leftshift", EventMask.LeftShift },
        { "rightshift", EventMask.RightShift },

        { "win", EventMask.Meta },
        { "meta", EventMask.Meta },
        { "leftmeta", EventMask.LeftMeta },
        { "rightmeta", EventMask.RightMeta },
    };

    /// <summary>
    ///     Converts a string representation of a keyboard key (e.g., "a", "b") 
    ///     into the corresponding SharpHook <see cref="KeyCode"/>.
    /// </summary>
    /// <param name="keyName">The string representation of the keyboard key from the configuration.</param>
    /// <returns>The mapped <see cref="KeyCode"/>, or null if the key is empty or not found.</returns>
    public static KeyCode? ParseMainKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
            return null;

        string normalized = keyName.Replace(" ", "").ToLowerInvariant();

        if (Enum.TryParse(normalized, true, out KeyCode directCode))
            return directCode;

        if (Enum.TryParse($"Vc{normalized}", true, out KeyCode prefixedCode))
            return prefixedCode;

        if (SymbolMap.TryGetValue(normalized, out KeyCode mappedCode))
            return mappedCode;

        return null;
    }

    /// <summary>
    ///     Converts a string representation of a modifier key (e.g., "alt", "ctrl") 
    ///     into the corresponding SharpHook <see cref="EventMask"/>.
    /// </summary>
    /// <param name="modifierName">The string representation of the modifier from the configuration.</param>
    /// <returns>The mapped <see cref="EventMask"/>, or null if the modifier is empty or not found.</returns>
    public static EventMask? ParseModifierKeyOrThrow(string modifierName)
    {
        if (string.IsNullOrWhiteSpace(modifierName))
            return null;

        string normalized = modifierName.Replace(" ", "").ToLowerInvariant();

        if (ModifierMap.TryGetValue(normalized, out EventMask mask))
            return mask;

        return null;
    }
}