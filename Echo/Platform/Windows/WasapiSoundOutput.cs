using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Echo.Platform.Windows;

/// <summary>
///     The loudspeakers of Windows, through the shared mode of the Windows audio engine.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WasapiSoundOutput : ISoundOutput
{
    /// <summary>
    ///     How far ahead the engine is fed: short enough for a sound to follow the key at once.
    /// </summary>
    private const int LatencyMilliseconds = 50;

    /// <inheritdoc/>
    public IDisposable Play(ISampleProvider source)
    {
        var output = new WasapiOut(AudioClientShareMode.Shared, LatencyMilliseconds);

        try
        {
            output.Init(source);
            output.Play();

            return output;
        }
        catch
        {
            output.Dispose();

            throw;
        }
    }
}