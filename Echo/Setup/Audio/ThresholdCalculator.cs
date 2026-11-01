namespace Echo.Setup.Audio;

/// <summary>
///     Works out the 'SilenceThreshold' setting from what the microphone actually picks up.
/// </summary>
internal static class ThresholdCalculator
{
    /// <summary>
    ///     The threshold used when calibration is skipped.
    /// </summary>
    public const float Default = 0.01f;

    private const float MinThreshold = 0.003f;
    private const float MaxThreshold = 0.05f;
    private const float NoiseMargin = 3f;
    private const float VoiceShare = 0.4f;
    private const float MinVoiceLevel = 0.015f;
    private const float MinVoiceToNoise = 2.5f;

    /// <summary>
    ///     The threshold sits well above the background noise, but never so high that it cuts off quiet speech.
    /// </summary>
    /// <param name="quiet">Peak levels sampled while the user stayed silent.</param>
    /// <param name="speech">Peak levels sampled while the user was speaking.</param>
    public static Calibration Calculate(IReadOnlyList<float> quiet, IReadOnlyList<float> speech)
    {
        // Percentiles, so a single click or cough does not decide the result
        float noise = Percentile(quiet, 0.95f);
        float voice = Percentile(speech, 0.9f);

        bool isVoiceHeard = voice >= Math.Max(MinVoiceLevel, noise * MinVoiceToNoise);

        float threshold = Math.Clamp(noise * NoiseMargin, MinThreshold, MaxThreshold);

        if (isVoiceHeard)
            threshold = Math.Max(MinThreshold, Math.Min(threshold, voice * VoiceShare));

        return new Calibration(noise, voice, threshold, isVoiceHeard);
    }

    private static float Percentile(IReadOnlyList<float> values, float share)
    {
        if (values.Count == 0)
            return 0f;

        float[] sorted = [.. values.Order()];

        return sorted[Math.Min(sorted.Length - 1, (int)Math.Floor(sorted.Length * share))];
    }
}