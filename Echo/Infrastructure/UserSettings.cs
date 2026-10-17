using Echo.Setup;

namespace Echo.Infrastructure;

/// <summary>
///     The choices made in the setup wizard.
/// </summary>
internal sealed record UserSettings
{
    /// <summary>
    ///     The version of the setup wizard that was completed; 0 when setup has never been finished.
    /// </summary>
    public int SetupVersion { get; init; }

    public string? UiLanguage { get; init; }

    public string? DictationLanguage { get; init; }

    /// <summary>
    ///     The settings of the "Additional settings" step that are present in the file.
    /// </summary>
    public AdditionalValues Additional { get; init; } = new();

    public string? HardwareBackend { get; init; }

    public int? GraphicsCardIndex { get; init; }

    public string? ModelName { get; init; }

    public string? HotkeyKey { get; init; }

    public string? HotkeyModifier { get; init; }

    public int? MicrophoneIndex { get; init; }

    public string? MicrophoneName { get; init; }

    public float? SilenceThreshold { get; init; }
}