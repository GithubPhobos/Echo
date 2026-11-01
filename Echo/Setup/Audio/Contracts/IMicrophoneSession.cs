namespace Echo.Setup.Audio.Contracts;

/// <summary>
///     An open microphone that reports how loud the input is.
/// </summary>
internal interface IMicrophoneSession : IDisposable
{
    /// <summary>
    ///     The loudest sample since the previous call, from 0 to 1.
    /// </summary>
    float ReadPeak();
}