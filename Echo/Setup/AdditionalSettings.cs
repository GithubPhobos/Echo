namespace Echo.Setup;

/// <summary>
///     The settings offered on the "Additional settings" step, in the order they are listed.
///     Everything else in the settings file is for editing by hand.
/// </summary>
internal static class AdditionalSettings
{
    public const string On = "true";
    public const string Off = "false";

    /// <summary>
    ///     Where the log level lives in the settings file: a section within a section.
    /// </summary>
    public const string LogLevelSection = "Serilog:MinimumLevel";

    public static readonly AdditionalSetting Translate = new(
        "Translate", SettingGroup.Recognition, nameof(WhisperSettings), nameof(WhisperSettings.Translate), SettingKind.Switch, Off)
    {
        HasCaution = true
    };

    public static readonly AdditionalSetting Prompt = new(
        "Prompt", SettingGroup.Recognition, nameof(WhisperSettings), nameof(WhisperSettings.Prompt), SettingKind.Text, Default: null);

    public static readonly AdditionalSetting CarryInitialPrompt = new(
        "CarryInitialPrompt", SettingGroup.Recognition, nameof(WhisperSettings), nameof(WhisperSettings.CarryInitialPrompt), SettingKind.Switch, Off);

    public static readonly AdditionalSetting NoContext = new(
        "NoContext", SettingGroup.Recognition, nameof(WhisperSettings), nameof(WhisperSettings.NoContext), SettingKind.Switch, Off);

    public static readonly AdditionalSetting SingleSegment = new(
        "SingleSegment", SettingGroup.Recognition, nameof(WhisperSettings), nameof(WhisperSettings.SingleSegment), SettingKind.Switch, On);

    public static readonly AdditionalSetting SuppressBlank = new(
        "SuppressBlank", SettingGroup.Recognition, nameof(WhisperSettings), nameof(WhisperSettings.SuppressBlank), SettingKind.Switch, On);

    public static readonly AdditionalSetting Temperature = new(
        "Temperature", SettingGroup.Recognition, nameof(WhisperSettings), nameof(WhisperSettings.Temperature), SettingKind.Fraction, Default: null, Min: 0, Max: 1)
    {
        Steps = ["0", "0.2", "0.4", "0.6", "0.8", "1"]
    };

    public static readonly AdditionalSetting Threads = new(
        "Threads", SettingGroup.Recognition, nameof(WhisperSettings), nameof(WhisperSettings.Threads), SettingKind.Whole, Default: null, Min: 1, Max: 64)
    {
        Steps = ["1", "2", "4", "6", "8", "12", "16"]
    };

    public static readonly AdditionalSetting SuppressRegex = new(
        "SuppressRegex", SettingGroup.Recognition, nameof(WhisperSettings), nameof(WhisperSettings.SuppressRegex), SettingKind.Text, Default: null);

    public static readonly AdditionalSetting BufferMs = new(
        "BufferMs", SettingGroup.Recording, nameof(RecordingDeviceSettings), nameof(RecordingDeviceSettings.BufferMs), SettingKind.Whole, "50", Min: 10, Max: 500)
    {
        Steps = ["20", "30", "50", "100", "200"]
    };

    public static readonly AdditionalSetting NumberOfBuffers = new(
        "NumberOfBuffers", SettingGroup.Recording, nameof(RecordingDeviceSettings), nameof(RecordingDeviceSettings.NumberOfBuffers), SettingKind.Whole, "3", Min: 2, Max: 10)
    {
        Steps = ["2", "3", "4", "5", "6", "8"]
    };

    public static readonly AdditionalSetting MicrophoneLevelOutput = new(
        "MicrophoneLevelOutput", SettingGroup.Recording, nameof(RecordingDeviceSettings), nameof(RecordingDeviceSettings.OutputMicAmplitudeDebugInfo), SettingKind.Switch, Off);

    public static readonly AdditionalSetting PlaySound = new(
        "PlaySound", SettingGroup.General, nameof(PushToTalkSettings), nameof(PushToTalkSettings.PlaySound), SettingKind.Switch, On);

    public static readonly AdditionalSetting Volume = new(
        "Volume", SettingGroup.General, nameof(PushToTalkSettings), nameof(PushToTalkSettings.Volume), SettingKind.Percent, "0.1", Min: 0, Max: 1)
    {
        Steps = ["0.05", "0.1", "0.2", "0.3", "0.5", "0.75", "1"]
    };

    public static readonly AdditionalSetting AutoInsert = new(
        "AutoInsert", SettingGroup.General, nameof(TextInsertionSettings), nameof(TextInsertionSettings.UseAutoInsert), SettingKind.Switch, On);

    public static readonly AdditionalSetting LogLevel = new(
        "LogLevel", SettingGroup.General, LogLevelSection, "Default", SettingKind.Choice, "Debug")
    {
        Steps = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"]
    };

    public static readonly IReadOnlyList<AdditionalSetting> All =
    [
        Translate,
        Prompt,
        CarryInitialPrompt,
        NoContext,
        SingleSegment,
        SuppressBlank,
        Temperature,
        Threads,
        SuppressRegex,
        BufferMs,
        NumberOfBuffers,
        MicrophoneLevelOutput,
        PlaySound,
        Volume,
        AutoInsert,
        LogLevel,
    ];
}