namespace Echo.Platform.Linux;

/// <inheritdoc cref="IPulseSimple"/>
[SupportedOSPlatform("linux")]
internal sealed class PulseSimple : IPulseSimple
{
    /// <inheritdoc/>
    public IntPtr Open(int direction,
                       string? device,
                       string streamName,
                       PulseAudioNative.SampleSpec sampleSpec,
                       PulseAudioNative.BufferAttributes attributes,
                       out int error) =>
        PulseAudioNative.SimpleNew(null, ApplicationInfo.CommandName, direction, device, streamName, ref sampleSpec, IntPtr.Zero, ref attributes, out error);

    /// <inheritdoc/>
    public int Read(IntPtr stream, byte[] piece, out int error) =>
        PulseAudioNative.SimpleRead(stream, piece, (UIntPtr)piece.Length, out error);

    /// <inheritdoc/>
    public int Write(IntPtr stream, byte[] piece, out int error) =>
        PulseAudioNative.SimpleWrite(stream, piece, (UIntPtr)piece.Length, out error);

    /// <inheritdoc/>
    public void Free(IntPtr stream) =>
        PulseAudioNative.SimpleFree(stream);

    /// <inheritdoc/>
    public string Describe(int error) =>
        PulseAudioNative.Describe(error);
}