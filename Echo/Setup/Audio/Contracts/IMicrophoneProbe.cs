namespace Echo.Setup.Audio.Contracts;

/// <summary>
///     Lists microphones and listens to them without recording anything.
/// </summary>
internal interface IMicrophoneProbe
{
    IReadOnlyList<MicrophoneDevice> GetDevices();

    IMicrophoneSession Open(int deviceIndex);
}