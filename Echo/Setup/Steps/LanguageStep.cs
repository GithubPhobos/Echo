using System.Globalization;
using Echo.UI;

namespace Echo.Setup.Steps;

/// <summary>
///     Step 1: the language of the interface and the language of dictation.
/// </summary>
internal sealed class LanguageStep : ISetupStep
{
    private readonly SetupUi _ui;

    /// <param name="ui"><see cref="SetupUi"/></param>
    public LanguageStep(SetupUi ui)
    {
        _ui = ui;

        Pages =
        [
            new SetupPage(ChooseInterfaceAsync),
            new SetupPage(ChooseDictationAsync, SubTitle: () => Strings.SubDictation),
        ];
    }

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public string Title => Strings.StepLanguages;

    /// <inheritdoc/>
    public IReadOnlyList<SetupPage> Pages { get; }

    /// <inheritdoc/>
    public IEnumerable<(string Label, string Value)> Summarize(SetupContext context)
    {
        yield return (
            Strings.SummaryInterfaceLanguage,
            Languages.Interface.FirstOrDefault(language => language.Code == context.UiLanguage).Name ?? string.Empty);

        yield return (Strings.SummaryDictationLanguage, Languages.DictationName(context.DictationLanguage));
    }

    /// <inheritdoc/>
    public string? Brief(SetupContext context)
    {
        if (context.UiLanguage is null)
            return null;

        return context.DictationLanguage is null
            ? context.UiLanguage
            : $"{context.UiLanguage}/{context.DictationLanguage}";
    }

    private async Task<SetupStepResult> ChooseInterfaceAsync(SetupContext context,
                                                             SetupScreen screen,
                                                             CancellationToken cancellationToken)
    {
        string preselected = context.UiLanguage ?? Languages.MatchInterface(CultureInfo.CurrentUICulture);

        // Every language is named in itself, so the right one can be found whatever the interface speaks now
        UiResult<string> picked = await _ui.SelectAsync(
            screen,
            Strings.LanguageInterfaceQuestion,
            [.. Languages.Interface.Select(language => new Choice<string>(language.Code, language.Name))],
            Math.Max(0, Languages.Interface.ToList().FindIndex(language => language.Code == preselected)),
            notes: null,
            cancellationToken);

        if (picked.IsBack)
            return SetupStepResult.Back;

        context.UiLanguage = picked.Value;
        Languages.Apply(picked.Value);

        return SetupStepResult.Next;
    }

    private async Task<SetupStepResult> ChooseDictationAsync(SetupContext context,
                                                             SetupScreen screen,
                                                             CancellationToken cancellationToken)
    {
        string preselected = context.DictationLanguage ?? context.UiLanguage![..2];

        List<Choice<string>> choices =
        [
            .. Languages.Dictation.Select(language => new Choice<string>(language.Code, language.Name)),
            new(Languages.AutoDetect, Strings.DictationAuto, Hint: Strings.DictationAutoHint),
        ];

        UiResult<string> picked = await _ui.SelectAsync(
            screen,
            Strings.LanguageDictationQuestion,
            choices,
            Math.Max(0, choices.FindIndex(choice => choice.Value == preselected)),
            [SetupUi.Dim(Spectre.Console.Markup.Escape(Strings.LanguageDictationNote))],
            cancellationToken);

        if (picked.IsBack)
            return SetupStepResult.Back;

        context.DictationLanguage = picked.Value;

        // Speech that is already English has nothing to be translated into
        if (picked.Value == Languages.English)
            context.Translate = false;

        return SetupStepResult.Next;
    }
}