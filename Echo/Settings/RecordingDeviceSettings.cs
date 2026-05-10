namespace Echo.Settings;

/// <summary>
///     Recording device settings.
/// </summary>
internal sealed class RecordingDeviceSettings
{
    /// <summary>
    ///     Index of the physical microphone.
    /// </summary>
    public int DeviceIndex { get; init; }

    /// <summary>
    ///     Minimum volume level (0.0 to 1.0) to start recording.
    /// </summary>
    public float? SilenceThreshold { get; init; }

    /// <summary>
    ///     Frequency of audio data chunks in milliseconds.
    /// </summary>
    public int BufferMs { get; init; }

    /// <summary>
    ///     Number of internal buffers allocated by the audio driver. Default is 3.
    /// </summary>
    public int NumberOfBuffers { get; init; }

    /// <summary>
    ///     Indicates whether microphone amplitude will be output in the console.
    /// </summary>
    /// <remarks>
    ///     Useful for debugging purposes to set correct value into RecordingDeviceSettings.SilenceThreshold.
    /// </remarks>
    public bool OutputMicAmplitudeDebugInfo { get; init; }
}
