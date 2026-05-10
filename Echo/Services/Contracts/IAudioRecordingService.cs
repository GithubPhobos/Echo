namespace Echo.Services.Contracts;

/// <summary>
///     Provides audio recording capabilities.
/// </summary>
internal interface IAudioRecordingService : IDisposable
{
    /// <summary>
    ///     Validates available audio devices against the configuration and initializes the hardware microphone.
    /// </summary>
    /// <remarks>
    ///     This method starts the hardware listener permanently to avoid initialization delays on subsequent recordings.
    /// </remarks>
    /// <exception cref="Exception">Thrown if no devices are found or the selected device index is invalid.</exception>
    void InititalizeAudioRecordingCapabilities();

    /// <summary>
    ///     Begins a new logical recording session.
    /// </summary>
    /// <remarks>
    ///     Instantly opens a new memory stream to capture incoming bytes from the already-running hardware.
    /// </remarks>
    void StartRecording();

    /// <summary>
    ///     Ends the current logical recording session and flushes captured data.
    /// </summary>
    /// <returns>A populated <see cref="MemoryStream"/> containing the WAV audio data, or null if no session was active.</returns>
    MemoryStream? StopRecording();
}