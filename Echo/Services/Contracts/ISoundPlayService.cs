namespace Echo.Services.Contracts;

/// <summary>
///     Manages the loading and low-latency playback of system audio cues.
/// </summary>
/// <remarks>
///      e.g., microphone activation and deactivation sounds, using the WASAPI audio engine.
/// </remarks>
internal interface ISoundPlayService : IDisposable
{
    /// <summary>
    ///     Validates the presence of required audio files.
    /// </summary>
    /// <remarks>
    ///     Then caches them into RAM, and initializes the WASAPI output device and audio mixer.
    ///     Gracefully degrades if no playback devices are found or files are missing.
    /// </remarks>
    void EnsureCanPlaySounds();

    /// <summary>
    ///     Plays the "start recording" audio cue asynchronously.
    /// </summary>
    Task PlayMicrophoneOnSoundAsync();

    /// <summary>
    ///     Plays the "stop recording" audio cue asynchronously.
    /// </summary>
    Task PlayMicrophoneOffSoundAsync();
}
