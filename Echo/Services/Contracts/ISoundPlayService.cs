namespace Echo.Services.Contracts;

/// <summary>
///     Manages the loading and low-latency playback of system audio cues.
/// </summary>
/// <remarks>
///      e.g., microphone activation and deactivation sounds, through the loudspeakers of the system.
/// </remarks>
internal interface ISoundPlayService : IDisposable
{
    /// <summary>
    ///     Validates the presence of required audio files.
    /// </summary>
    /// <remarks>
    ///     Then caches them into RAM, and opens the loudspeakers with an audio mixer.
    ///     Gracefully degrades if no playback devices are found or files are missing.
    /// </remarks>
    void EnsureCanPlaySounds();

    /// <summary>
    ///     Closes the loudspeakers and opens them again, as the system may have closed them.
    /// </summary>
    /// <remarks>
    ///     Never throws: loudspeakers that do not open leave the sounds off until the next time.
    ///     Does nothing when the sounds are off in the settings or their files are missing.
    /// </remarks>
    void ReopenSounds();

    /// <summary>
    ///     Plays the "start recording" audio cue asynchronously.
    /// </summary>
    Task PlayMicrophoneOnSoundAsync();

    /// <summary>
    ///     Plays the "stop recording" audio cue asynchronously.
    /// </summary>
    Task PlayMicrophoneOffSoundAsync();
}
