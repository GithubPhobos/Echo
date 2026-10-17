using System.Globalization;
using Echo.Setup.Models;
using Echo.UI;
using Spectre.Console;

namespace Echo.Setup.Steps;

/// <summary>
///     Step 6: the settings nobody has to touch, listed in blocks. Space moves the highlighted setting on
///     to its next value, Enter opens a value for typing; the row the list starts on simply leads onwards.
/// </summary>
internal sealed class AdditionalSettingsStep : ISetupStep
{
    private const int ShownTextLength = 30;
    private const string CutMark = "...";

    private const string NumberFormat = "0.###";
    private const int PercentScale = 100;

    private readonly SetupUi _ui;

    /// <param name="ui"><see cref="SetupUi"/></param>
    public AdditionalSettingsStep(SetupUi ui)
    {
        _ui = ui;

        Pages = [new SetupPage(ChooseAsync)];
    }

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public string Title => Strings.StepAdditional;

    /// <inheritdoc/>
    public IReadOnlyList<SetupPage> Pages { get; }

    /// <inheritdoc/>
    /// <remarks>
    ///     Nothing: the review shows the choices nobody can do without, and these are one Escape away from it.
    /// </remarks>
    public IEnumerable<(string Label, string Value)> Summarize(SetupContext context) => [];

    /// <inheritdoc/>
    public string? Brief(SetupContext context) => null;

    /// <summary>
    ///     Speech that is already English has nothing to be translated into.
    /// </summary>
    private static bool IsOffered(AdditionalSetting setting, SetupContext context) =>
        setting != AdditionalSettings.Translate || context.DictationLanguage != Languages.English;

    private async Task<SetupStepResult> ChooseAsync(SetupContext context,
                                                    SetupScreen screen,
                                                    CancellationToken cancellationToken)
    {
        // Enter on every other screen means "go on", and so it does here: the row that leads onwards is
        // highlighted from the start, while the list above it is shown from its first setting
        var position = new ListPosition(BuildChoices(context).Count - 1);

        while (true)
        {
            UiResult<(AdditionalSetting? Value, ConsoleKey Key)> picked = await _ui.SelectAsync(
                screen,
                Strings.AdditionalQuestion,
                BuildChoices(context),
                position,
                DescribeState(context),
                KeysOf,
                cancellationToken);

            if (picked.IsBack)
                return SetupStepResult.Back;

            (AdditionalSetting? setting, ConsoleKey key) = picked.Value;

            if (setting is null)
                return SetupStepResult.Next;

            if (IsTyped(setting, key))
                await EditAsync(context, screen, setting, cancellationToken);
            else
                context.Additional.Step(setting);
        }
    }

    /// <summary>
    ///     The keys a row answers to. Null stands for the row that leads onwards.
    /// </summary>
    private static IReadOnlyList<ListKey> KeysOf(AdditionalSetting? setting) => setting?.Kind switch
    {
        null => [new(ConsoleKey.Enter, Strings.HintContinue)],

        // There is nothing to step through in a text: either key opens it
        SettingKind.Text => [new(ConsoleKey.Enter, Strings.HintType), new(ConsoleKey.Spacebar, Hint: null)],

        // Nothing to type either: both keys move on to the next value
        SettingKind.Switch or SettingKind.Choice => [new(ConsoleKey.Spacebar, Strings.HintSwitch), new(ConsoleKey.Enter, Hint: null)],

        _ => [new(ConsoleKey.Spacebar, Strings.HintSwitch), new(ConsoleKey.Enter, Strings.HintType)]
    };

    /// <summary>
    ///     Whether <paramref name="key"/> opens the setting for typing instead of moving it on to its next value.
    /// </summary>
    private static bool IsTyped(AdditionalSetting setting, ConsoleKey key) => setting.Kind switch
    {
        SettingKind.Text => true,
        SettingKind.Switch or SettingKind.Choice => false,
        _ => key == ConsoleKey.Enter
    };

    /// <summary>
    ///     Lets the user type a new value over the one in effect. Leaving the field empty returns the setting
    ///     to the shipped value; Escape leaves it as it was.
    /// </summary>
    private async Task EditAsync(SetupContext context, SetupScreen screen, AdditionalSetting setting, CancellationToken cancellationToken)
    {
        UiResult<string> typed = await _ui.InputAsync(
            screen,
            setting.Label,
            [SetupUi.Dim(Markup.Escape(setting.Hint)), SetupUi.Dim(Markup.Escape(Strings.InputEmptyNote))],
            ToTyped(setting, context.Additional.Get(setting)),
            text => Check(setting, text),
            cancellationToken);

        if (!typed.IsBack)
            context.Additional.Set(setting, FromTyped(setting, typed.Value));
    }

    private static List<Choice<AdditionalSetting?>> BuildChoices(SetupContext context)
    {
        List<Choice<AdditionalSetting?>> choices =
        [
            .. AdditionalSettings.All
                .Where(setting => IsOffered(setting, context))
                .Select(setting => new Choice<AdditionalSetting?>(
                    setting,
                    setting.Label,
                    Summary: ShowMarkup(setting, context.Additional),
                    Hint: setting.Hint)
                {
                    Group = GroupName(setting.Group),
                    Caution = setting.Caution
                }),
        ];

        choices.Add(new(null, Strings.AdditionalContinue, Hint: Strings.AdditionalContinueHint) { IsSetApart = true });

        return choices;
    }

    private static List<string> DescribeState(SetupContext context)
    {
        List<string> notes = [SetupUi.Dim(Markup.Escape(Strings.AdditionalNote))];

        // A switch that would do nothing with the chosen model is not left to be found out later
        if (IsOffered(AdditionalSettings.Translate, context) && context.Translate && UntranslatingModel(context) is { } model)
            notes.Add(SetupUi.Warning(Markup.Escape(Strings.Format(nameof(Strings.AdditionalTranslateWarning), model.Id))));

        return notes;
    }

    /// <summary>
    ///     The chosen model, when it is one that was not trained to translate.
    /// </summary>
    private static ModelInfo? UntranslatingModel(SetupContext context) =>
        ModelCatalog.All.FirstOrDefault(model =>
            model.FileName == context.ModelFileName && (model.Tier == ModelCatalog.LargeTurbo || model.IsEnglishOnly));

    private static string GroupName(SettingGroup group) => group switch
    {
        SettingGroup.Recognition => Strings.GroupRecognition,
        SettingGroup.Recording => Strings.GroupRecording,
        _ => Strings.GroupGeneral
    };

    /// <summary>
    ///     The value in effect, the way a person reads it.
    /// </summary>
    private static string Show(AdditionalSetting setting, AdditionalValues values)
    {
        string? value = values.Get(setting);

        if (setting.Kind == SettingKind.Switch)
            return value == AdditionalSettings.On ? Strings.ValueOn : Strings.ValueOff;

        if (value is null)
            return setting.Kind == SettingKind.Text ? Strings.ValueNotSet : Strings.ValueAuto;

        return setting.Kind switch
        {
            SettingKind.Text => value.Length <= ShownTextLength ? value : value[..ShownTextLength].TrimEnd() + CutMark,
            SettingKind.Percent => $"{ToTyped(setting, value)} %",
            _ => ToTyped(setting, value)
        };
    }

    private static string ShowMarkup(AdditionalSetting setting, AdditionalValues values)
    {
        string shown = Markup.Escape(Show(setting, values));

        // What the user has changed stands out from what was left as it ships
        return values.Stored(setting) is { } stored && stored != setting.Default
            ? SetupUi.Done(shown)
            : shown;
    }

    /// <summary>
    ///     The kept value as the user types it: in the number format of the interface language, a share as percent.
    /// </summary>
    private static string ToTyped(AdditionalSetting setting, string? value)
    {
        if (value is null)
            return string.Empty;

        return setting.Kind switch
        {
            SettingKind.Fraction => Number(value).ToString(NumberFormat, CultureInfo.CurrentUICulture),
            SettingKind.Percent => Math.Round(Number(value) * PercentScale).ToString(CultureInfo.CurrentUICulture),
            _ => value
        };
    }

    /// <summary>
    ///     What the user typed, in the form the value is kept in. The text has passed <see cref="Check"/>.
    /// </summary>
    private static string? FromTyped(AdditionalSetting setting, string typed)
    {
        typed = typed.Trim();

        if (typed.Length == 0)
            return null;

        return setting.Kind switch
        {
            SettingKind.Whole => ((int)Parse(typed)!.Value).ToString(CultureInfo.InvariantCulture),
            SettingKind.Fraction => Parse(typed)!.Value.ToString(NumberFormat, CultureInfo.InvariantCulture),
            SettingKind.Percent => (Parse(typed)!.Value / PercentScale).ToString(NumberFormat, CultureInfo.InvariantCulture),
            _ => typed
        };
    }

    /// <summary>
    ///     Returns what is wrong with the typed text, or null when it can be kept.
    /// </summary>
    private static string? Check(AdditionalSetting setting, string typed)
    {
        typed = typed.Trim();

        if (typed.Length == 0 || setting.Kind == SettingKind.Text)
            return null;

        double scale = setting.Kind == SettingKind.Percent ? PercentScale : 1;
        bool isWhole = setting.Kind is SettingKind.Whole or SettingKind.Percent;

        double? number = Parse(typed);

        bool isValid = number is double value
            && value >= setting.Min * scale
            && value <= setting.Max * scale
            && (!isWhole || value == Math.Floor(value));

        if (isValid)
            return null;

        return Strings.Format(
            isWhole ? nameof(Strings.InputWholeError) : nameof(Strings.InputFractionError),
            setting.Min * scale,
            setting.Max * scale);
    }

    /// <summary>
    ///     Reads a number typed with either a point or a comma before the fraction.
    /// </summary>
    private static double? Parse(string typed) =>
        double.TryParse(typed.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            ? number
            : null;

    private static double Number(string value) =>
        double.Parse(value, CultureInfo.InvariantCulture);
}