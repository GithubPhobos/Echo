using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Echo.Setup.Audio;
using Echo.Setup.Hardware;
using Echo.Setup.Hotkeys;

namespace Echo.Setup;

/// <summary>
///     The choices collected by the wizard so far. Nothing here is saved until the wizard finishes.
/// </summary>
internal sealed class SetupContext
{
    public string? UiLanguage { get; set; }

    public string? DictationLanguage { get; set; }

    /// <summary>
    ///     The values of the "Additional settings" step.
    /// </summary>
    public AdditionalValues Additional { get; init; } = new();

    /// <summary>
    ///     Whether what is said is typed in English, whatever language it is said in. One of the additional
    ///     settings, kept at hand because the choice of a model depends on it.
    /// </summary>
    public bool Translate
    {
        get => Additional.IsOn(AdditionalSettings.Translate);
        set => Additional.Set(AdditionalSettings.Translate, value ? AdditionalSettings.On : AdditionalSettings.Off);
    }

    /// <summary>
    ///     Detected once and reused by the acceleration and the model steps.
    /// </summary>
    public HardwareInfo? Hardware { get; set; }

    /// <summary>
    ///     The number of the NVIDIA graphics card to use, when the machine has several.
    /// </summary>
    public int? GraphicsCardIndex { get; set; }

    /// <summary>
    ///     What was picked in the list of backends.
    /// </summary>
    public string HardwareBackend { get; set; } = Acceleration.Auto;

    public string? ModelFileName { get; set; }

    public HotkeyChoice Hotkey { get; set; } = HotkeyChoice.Default;

    /// <summary>
    ///     How the desktop describes the key it gave, where the desktop assigns the key itself (Wayland).
    /// </summary>
    public string? HotkeyLabel { get; set; }

    public int MicrophoneIndex { get; set; }

    public string? MicrophoneName { get; set; }

    /// <summary>
    ///     The level above which input counts as speech. One of the additional settings, kept at hand because
    ///     the sound step measures it.
    /// </summary>
    public float SilenceThreshold
    {
        get => float.TryParse(Additional.Get(AdditionalSettings.SilenceThreshold), NumberStyles.Float, CultureInfo.InvariantCulture, out float level)
            ? level
            : ThresholdCalculator.Default;
        set => Additional.Set(AdditionalSettings.SilenceThreshold, value.ToString(ThresholdFormat, CultureInfo.InvariantCulture));
    }

    /// <summary>
    ///     Enough digits for a measured level, and none of the noise of a number stored in binary.
    /// </summary>
    private const string ThresholdFormat = "0.######";

    /// <summary>
    ///     The backend picked in the list, with "automatic" resolved to the best one the machine offers.
    /// </summary>
    public AccelerationBackend RequestedBackend =>
        Acceleration.Parse(HardwareBackend)
        ?? (Hardware is null ? AccelerationBackend.Cpu : Acceleration.Best(Hardware));

    /// <summary>
    ///     Keeps what the hardware probe found. A card picked in an earlier setup stays picked, as long as it is still there.
    /// </summary>
    [MemberNotNull(nameof(Hardware))]
    public void UseDetected(HardwareInfo detected)
    {
        NvidiaGpu? remembered = detected.NvidiaCards.FirstOrDefault(card => card.Index == GraphicsCardIndex);

        Hardware = remembered is null ? detected : detected with { Nvidia = remembered };
    }

    public static SetupContext FromSettings(UserSettings settings) => new()
    {
        UiLanguage = settings.UiLanguage,
        DictationLanguage = settings.DictationLanguage,
        Additional = settings.Additional.Clone(),
        HardwareBackend = settings.HardwareBackend ?? Acceleration.Auto,
        GraphicsCardIndex = settings.GraphicsCardIndex,
        ModelFileName = settings.ModelName,
        Hotkey = settings.HotkeyKey is null
            ? HotkeyChoice.Default
            : new HotkeyChoice(settings.HotkeyKey, settings.HotkeyModifier ?? string.Empty),
        HotkeyLabel = settings.HotkeyLabel,
        MicrophoneIndex = settings.MicrophoneIndex ?? 0,
        MicrophoneName = settings.MicrophoneName,
    };

    public UserSettings ToSettings(int setupVersion) => new()
    {
        SetupVersion = setupVersion,
        UiLanguage = UiLanguage,
        DictationLanguage = DictationLanguage,
        Additional = Additional.Clone(),
        HardwareBackend = HardwareBackend,
        GraphicsCardIndex = GraphicsCardIndex,
        ModelName = ModelFileName,
        HotkeyKey = Hotkey.Key,
        HotkeyModifier = Hotkey.Modifier,
        HotkeyLabel = HotkeyLabel,
        MicrophoneIndex = MicrophoneIndex,
        MicrophoneName = MicrophoneName,
    };
}