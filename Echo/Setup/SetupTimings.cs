namespace Echo.Setup;

/// <summary>
///     How long the timed parts of the wizard take. Shortened in tests.
/// </summary>
/// <param name="Tick">How often live screens refresh.</param>
/// <param name="QuietDuration">How long the background noise is measured.</param>
/// <param name="SpeechDuration">How long the user's voice is measured.</param>
/// <param name="DeviceTimeout">How long a microphone may take to open before it is given up on.</param>
internal sealed record SetupTimings(TimeSpan Tick, TimeSpan QuietDuration, TimeSpan SpeechDuration, TimeSpan DeviceTimeout)
{
    public static SetupTimings Default { get; } = new(
        TimeSpan.FromMilliseconds(60),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8));
}