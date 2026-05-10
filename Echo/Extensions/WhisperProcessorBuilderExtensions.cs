using Whisper.net;

/// <summary>
///     Provides fluent extension methods for <see cref="WhisperProcessorBuilder"/> 
///     to easily map custom configuration settings.
/// </summary>
internal static class WhisperProcessorBuilderExtensions
{
    /// <summary>
    ///     Applies all configured properties from the <see cref="WhisperSettings"/> object 
    ///     to the <see cref="WhisperProcessorBuilder"/> instance.
    /// </summary>
    /// <param name="builder">The builder instance to configure.</param>
    /// <param name="settings">The strongly-typed Whisper settings from the configuration.</param>
    /// <returns><see cref="WhisperProcessorBuilder"/></returns>
    public static WhisperProcessorBuilder ApplySettings(this WhisperProcessorBuilder builder,
                                                        WhisperSettings settings)
    {
        if (settings.Threads is not null && settings.Threads > 0)
            builder.WithThreads(settings.Threads.Value);

        builder.WithPrintTimestamps(settings.PrintTimestamps);

        if (string.Equals(settings.Language, "auto", StringComparison.OrdinalIgnoreCase))
            builder.WithLanguageDetection();
        else
            builder.WithLanguage(settings.Language);

        if (settings.Translate)
            builder.WithTranslate();

        if (settings.NoContext)
            builder.WithNoContext();

        if (settings.SingleSegment)
            builder.WithSingleSegment();

        if (settings.PrintSpecialTokens)
            builder.WithPrintSpecialTokens();

        if (settings.PrintProgress)
            builder.WithPrintProgress();

        if (settings.PrintResults)
            builder.WithPrintResults();

        if (settings.UseTokenTimestamps)
            builder.WithTokenTimestamps();

        if (settings.SplitOnWord)
            builder.SplitOnWord();

        if (settings.ComputeProbabilities)
            builder.WithProbabilities();

        if (!settings.SuppressBlank)
            builder.WithoutSuppressBlank();

        if (settings.CarryInitialPrompt)
            builder.WithCarryInitialPrompt();

        if (!string.IsNullOrWhiteSpace(settings.Prompt))
            builder.WithPrompt(settings.Prompt);

        if (!string.IsNullOrWhiteSpace(settings.SuppressRegex))
            builder.WithSuppressRegex(settings.SuppressRegex);

        if (settings.Offset.HasValue)
            builder.WithOffset(settings.Offset.Value);

        if (settings.Duration.HasValue)
            builder.WithDuration(settings.Duration.Value);

        if (settings.MaxLastTextTokens.HasValue)
            builder.WithMaxLastTextTokens(settings.MaxLastTextTokens.Value);

        if (settings.TokenTimestampsThreshold.HasValue)
            builder.WithTokenTimestampsThreshold(settings.TokenTimestampsThreshold.Value);

        if (settings.TokenTimestampsSumThreshold.HasValue)
            builder.WithTokenTimestampsSumThreshold(settings.TokenTimestampsSumThreshold.Value);

        if (settings.MaxSegmentLength.HasValue)
            builder.WithMaxSegmentLength(settings.MaxSegmentLength.Value);

        if (settings.MaxTokensPerSegment.HasValue)
            builder.WithMaxTokensPerSegment(settings.MaxTokensPerSegment.Value);

        if (settings.AudioContextSize.HasValue)
            builder.WithAudioContextSize(settings.AudioContextSize.Value);

        if (settings.Temperature.HasValue)
            builder.WithTemperature(settings.Temperature.Value);

        if (settings.TemperatureInc.HasValue)
            builder.WithTemperatureInc(settings.TemperatureInc.Value);

        if (settings.MaxInitialTs.HasValue)
            builder.WithMaxInitialTs(settings.MaxInitialTs.Value);

        if (settings.LengthPenalty.HasValue)
            builder.WithLengthPenalty(settings.LengthPenalty.Value);

        if (settings.EntropyThreshold.HasValue)
            builder.WithEntropyThreshold(settings.EntropyThreshold.Value);

        if (settings.LogProbThreshold.HasValue)
            builder.WithLogProbThreshold(settings.LogProbThreshold.Value);

        if (settings.NoSpeechThreshold.HasValue)
            builder.WithNoSpeechThreshold(settings.NoSpeechThreshold.Value);

        // Force Greedy strategy for real-time performance
        builder.WithGreedySamplingStrategy();

        if (!string.IsNullOrWhiteSpace(settings.OpenVinoEncoderPath))
        {
            builder.WithOpenVinoEncoder(
                settings.OpenVinoEncoderPath,
                settings.OpenVinoDevice,
                settings.OpenVinoCacheDir);
        }

        return builder;
    }
}