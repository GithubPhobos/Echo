using System.Globalization;

namespace Echo.Setup.Steps;

/// <summary>
///     The languages the interface is translated into and the ones offered for dictation.
/// </summary>
internal static class Languages
{
    public const string English = "en";
    public const string AutoDetect = "auto";

    /// <summary>
    ///     Interface languages: culture name and the language's own name.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Interface =
    [
        ("en", "English"),
        ("ru", "Русский"),
        ("zh-Hans", "简体中文"),
        ("es", "Español"),
        ("de", "Deutsch"),
    ];

    /// <summary>
    ///     Dictation languages: Whisper language code and the language's own name. Whisper knows many more;
    ///     any of them can be put into the settings file.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Dictation =
    [
        ("en", "English"),
        ("ru", "Русский"),
        ("zh", "中文"),
        ("es", "Español"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("pt", "Português"),
        ("it", "Italiano"),
        ("ja", "日本語"),
        ("ko", "한국어"),
        ("pl", "Polski"),
        ("tr", "Türkçe"),
        ("nl", "Nederlands"),
    ];

    /// <summary>
    ///     The interface language closest to the language of the operating system.
    /// </summary>
    public static string MatchInterface(CultureInfo culture)
    {
        if (culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-Hans";

        return Interface.Any(language => language.Code == culture.TwoLetterISOLanguageName)
            ? culture.TwoLetterISOLanguageName
            : English;
    }

    /// <summary>
    ///     Switches the interface to the given language for the rest of the run.
    /// </summary>
    public static void Apply(string? code)
    {
        if (!Interface.Any(language => language.Code == code))
            return;

        var culture = CultureInfo.GetCultureInfo(code!);

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    public static string DictationName(string? code)
    {
        if (code == AutoDetect)
            return Strings.DictationAutoName;

        return Dictation.FirstOrDefault(language => language.Code == code).Name ?? code ?? string.Empty;
    }
}