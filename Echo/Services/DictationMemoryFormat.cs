using Microsoft.Extensions.Configuration;

namespace Echo.Services;

/// <summary>
///     Stable codes for the history. Codes are explicit, never derived from catalog order or reused.
/// </summary>
internal static class DictationMemoryFormat
{
    /// <summary>
    ///     Known model names without their optional ggml prefix and bin extension.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Models = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["tiny"] = "m1", ["tiny.en"] = "m2", ["base"] = "m3", ["base.en"] = "m4",
        ["small"] = "m5", ["small.en"] = "m6", ["medium"] = "m7", ["medium.en"] = "m8",
        ["large-v3-turbo"] = "m9", ["large-v3"] = "m10",
    };

    /// <summary>
    ///     Whisper settings other than model, language, translation and requested backend, which have their own fields.
    ///     Boolean codes begin with e; the reverse mapping is documented in the Readme.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Settings = new Dictionary<string, string>
    {
        [nameof(WhisperSettings.GpuDeviceIndex)] = "gi",
        [nameof(WhisperSettings.Threads)] = "th",
        [nameof(WhisperSettings.NoContext)] = "enc",
        [nameof(WhisperSettings.SingleSegment)] = "ess",
        [nameof(WhisperSettings.PrintSpecialTokens)] = "est",
        [nameof(WhisperSettings.PrintProgress)] = "ep",
        [nameof(WhisperSettings.PrintResults)] = "er",
        [nameof(WhisperSettings.PrintTimestamps)] = "ets",
        [nameof(WhisperSettings.UseTokenTimestamps)] = "ett",
        [nameof(WhisperSettings.SplitOnWord)] = "esw",
        [nameof(WhisperSettings.ComputeProbabilities)] = "ecp",
        [nameof(WhisperSettings.SuppressBlank)] = "esb",
        [nameof(WhisperSettings.Prompt)] = "p",
        [nameof(WhisperSettings.CarryInitialPrompt)] = "eip",
        [nameof(WhisperSettings.SuppressRegex)] = "sr",
        [nameof(WhisperSettings.Offset)] = "o",
        [nameof(WhisperSettings.Duration)] = "d",
        [nameof(WhisperSettings.MaxLastTextTokens)] = "mlt",
        [nameof(WhisperSettings.TokenTimestampsThreshold)] = "tt",
        [nameof(WhisperSettings.TokenTimestampsSumThreshold)] = "tts",
        [nameof(WhisperSettings.MaxSegmentLength)] = "msl",
        [nameof(WhisperSettings.MaxTokensPerSegment)] = "mt",
        [nameof(WhisperSettings.AudioContextSize)] = "ac",
        [nameof(WhisperSettings.Temperature)] = "t",
        [nameof(WhisperSettings.TemperatureInc)] = "ti",
        [nameof(WhisperSettings.MaxInitialTs)] = "it",
        [nameof(WhisperSettings.LengthPenalty)] = "lp",
        [nameof(WhisperSettings.EntropyThreshold)] = "en",
        [nameof(WhisperSettings.LogProbThreshold)] = "lpb",
        [nameof(WhisperSettings.NoSpeechThreshold)] = "ns",
        [nameof(WhisperSettings.OpenVinoEncoderPath)] = "ve",
        [nameof(WhisperSettings.OpenVinoDevice)] = "vd",
        [nameof(WhisperSettings.OpenVinoCacheDir)] = "vc",
    };

    /// <summary>
    ///     A known model's code, or its original name when it is not in the fixed table.
    /// </summary>
    internal static string? ModelCode(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        string normalized = Path.GetFileName(name);
        if (normalized.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];
        if (normalized.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[5..];

        return Models.GetValueOrDefault(normalized) ?? name;
    }

    /// <summary>
    ///     Reads this version's bundled defaults, without user overrides.
    /// </summary>
    internal static WhisperSettings Defaults() => new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .Build()
        .GetSection(nameof(WhisperSettings)).Get<WhisperSettings>() ?? new WhisperSettings();

    /// <summary>
    ///     Captures only effective differences, once per service instance rather than once per phrase.
    ///     Explicit null values are retained when they differ from a bundled value.
    /// </summary>
    internal static IReadOnlyDictionary<string, object?> CustomSettings(WhisperSettings current, WhisperSettings defaults)
    {
        var changes = new Dictionary<string, object?>();
        foreach ((string property, string code) in Settings)
        {
            var member = typeof(WhisperSettings).GetProperty(property)!;
            object? value = member.GetValue(current);
            if (!Equals(value, member.GetValue(defaults)))
                changes[code] = value;
        }
        return changes;
    }
}