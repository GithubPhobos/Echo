namespace Echo.Platform;

/// <summary>
///     A piece of sound from a microphone: 16 kHz, 16-bit, mono.
/// </summary>
/// <param name="buffer">The sound; only the first <paramref name="bytesRecorded"/> bytes count.</param>
/// <param name="bytesRecorded">How many bytes of <paramref name="buffer"/> hold sound.</param>
internal sealed class AudioInputEventArgs(byte[] buffer, int bytesRecorded) : EventArgs
{
    public byte[] Buffer { get; } = buffer;

    public int BytesRecorded { get; } = bytesRecorded;
}