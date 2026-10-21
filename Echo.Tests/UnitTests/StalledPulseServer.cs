using Echo.Platform.Linux;

namespace Echo.Tests.UnitTests;

/// <summary>
///     A sound server whose reads and writes wait until the test lets them go, the way a server that stopped
///     answering holds them, and that counts the streams it opens and frees.
/// </summary>
internal sealed class StalledPulseServer : IPulseSimple
{
    private int _opened;
    private int _freed;

    /// <summary>
    ///     Set once a read or a write is waiting for the server.
    /// </summary>
    public ManualResetEventSlim Entered { get; } = new();

    /// <summary>
    ///     Lets every read and write go, now and from then on.
    /// </summary>
    public ManualResetEventSlim Answering { get; } = new();

    public int Opened => Volatile.Read(ref _opened);

    public int Freed => Volatile.Read(ref _freed);

    public IntPtr Open(int direction,
                       string? device,
                       string streamName,
                       PulseAudioNative.SampleSpec sampleSpec,
                       PulseAudioNative.BufferAttributes attributes,
                       out int error)
    {
        error = 0;

        return Interlocked.Increment(ref _opened);
    }

    public int Read(IntPtr stream, byte[] piece, out int error) => Wait(out error);

    public int Write(IntPtr stream, byte[] piece, out int error) => Wait(out error);

    public void Free(IntPtr stream) => Interlocked.Increment(ref _freed);

    public string Describe(int error) => "stalled";

    /// <summary>
    ///     Holds the call until the server answers; the limit only keeps a broken test from hanging.
    /// </summary>
    private int Wait(out int error)
    {
        error = 0;
        Entered.Set();
        Answering.Wait(TimeSpan.FromSeconds(30));

        return 0;
    }
}