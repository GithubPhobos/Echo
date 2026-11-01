namespace Echo.Setup.Audio;

/// <summary>
///     The outcome of listening to silence and then to speech.
/// </summary>
/// <param name="NoiseLevel">How loud the room is when nobody speaks.</param>
/// <param name="VoiceLevel">How loud the user's speech is.</param>
/// <param name="Threshold">The level above which input counts as speech.</param>
/// <param name="IsVoiceHeard">Whether the speech clearly stood out from the background.</param>
internal readonly record struct Calibration(float NoiseLevel, float VoiceLevel, float Threshold, bool IsVoiceHeard);