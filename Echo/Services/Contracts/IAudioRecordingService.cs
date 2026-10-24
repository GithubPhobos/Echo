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
    ///     Marks the microphone to be opened again, as the system may have closed it. Returns at once: the opening is
    ///     done by <see cref="ReopenMicrophone"/>, or by the next recording if that begins first.
    /// </summary>
    void MarkMicrophoneForReopening();

    /// <summary>
    ///     Closes the microphone marked for it and opens it again, found by its name.
    /// </summary>
    /// <remarks>
    ///     Never throws: a microphone that does not open is reported on the screen and in the log, and tried again at
    ///     the next recording.
    ///     Does nothing when the microphone is not marked any more, before it was first opened and after the service is
    ///     closed.
    /// </remarks>
    /// <returns>
    ///     False when a recording that began before the mark has the microphone: opened again then, the recording would
    ///     be cut. It stays marked, for a call after that recording or for the next recording to open it again.
    /// </returns>
    bool ReopenMicrophone();

    /// <summary>
    ///     Begins a new logical recording session.
    /// </summary>
    /// <remarks>
    ///     Instantly opens a new memory stream to capture incoming bytes from the already-running hardware.
    ///     A microphone marked to be opened again is opened again first, and one that is not open is tried once more.
    /// </remarks>
    /// <returns>
    ///     Whether the recording began: without its microphone nothing is recorded, and the screen is told why.
    /// </returns>
    bool StartRecording();

    /// <summary>
    ///     Ends the current logical recording session and flushes captured data.
    /// </summary>
    /// <returns>A populated <see cref="MemoryStream"/> containing the WAV audio data, or null if no session was active.</returns>
    MemoryStream? StopRecording();
}