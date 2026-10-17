using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Echo.Platform.Linux;

/// <summary>
///     The client library of PulseAudio, the sound server of Linux desktops. PipeWire, which newer desktops run
///     instead, speaks the same language, and so does the sound server of WSL. The library is part of every
///     desktop install ('libpulse0' on Ubuntu and Debian, 'pulseaudio-libs' on Fedora, 'libpulse' on Arch).
/// </summary>
[SupportedOSPlatform("linux")]
internal static class PulseAudioNative
{
    private const string SimpleLibrary = "libpulse-simple.so.0";
    private const string Library = "libpulse.so.0";

    /// <summary>
    ///     PA_STREAM_PLAYBACK and PA_STREAM_RECORD.
    /// </summary>
    public const int Playback = 1;
    public const int Record = 2;

    /// <summary>
    ///     PA_SAMPLE_S16LE and PA_SAMPLE_FLOAT32LE.
    /// </summary>
    public const int Signed16 = 3;
    public const int Float32 = 5;

    /// <summary>
    ///     PA_CONTEXT_NOAUTOSPAWN: a missing sound server is reported, not started.
    /// </summary>
    public const int NoAutospawn = 1;

    /// <summary>
    ///     PA_CONTEXT_READY, PA_CONTEXT_FAILED and PA_CONTEXT_TERMINATED.
    /// </summary>
    public const int ContextReady = 4;
    public const int ContextFailed = 5;
    public const int ContextTerminated = 6;

    /// <summary>
    ///     PA_OPERATION_RUNNING.
    /// </summary>
    public const int OperationRunning = 0;

    /// <summary>
    ///     PA_INVALID_INDEX, also "let the server choose" in buffer sizes.
    /// </summary>
    public const uint Invalid = uint.MaxValue;

    /// <summary>
    ///     The place of 'monitor_of_sink' in pa_source_info on 64-bit systems: after the name (8), the index (4 and 4 of
    ///     padding), the description (8), the sample spec (12), the channel map (132), the owning module (4), the volume
    ///     (132) and the mute switch (4).
    /// </summary>
    public const int MonitorOfSinkOffset = 308;

    /// <summary>
    ///     The place of 'default_source_name' in pa_server_info on 64-bit systems: after four strings (32),
    ///     the sample spec (12 and 4 of padding) and the default sink name (8).
    /// </summary>
    public const int DefaultSourceOffset = 56;

    [StructLayout(LayoutKind.Sequential)]
    public struct SampleSpec
    {
        public int Format;
        public uint Rate;
        public byte Channels;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BufferAttributes
    {
        public uint MaxLength;
        public uint TargetLength;
        public uint Prebuffer;
        public uint MinimumRequest;
        public uint FragmentSize;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SourceInfoCallback(IntPtr context, IntPtr info, int isLast, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ServerInfoCallback(IntPtr context, IntPtr info, IntPtr userData);

    [DllImport(SimpleLibrary, EntryPoint = "pa_simple_new")]
    public static extern IntPtr SimpleNew(string? server,
                                          string name,
                                          int direction,
                                          string? device,
                                          string streamName,
                                          ref SampleSpec sampleSpec,
                                          IntPtr channelMap,
                                          ref BufferAttributes bufferAttributes,
                                          out int error);

    [DllImport(SimpleLibrary, EntryPoint = "pa_simple_free")]
    public static extern void SimpleFree(IntPtr simple);

    [DllImport(SimpleLibrary, EntryPoint = "pa_simple_read")]
    public static extern int SimpleRead(IntPtr simple, byte[] data, UIntPtr bytes, out int error);

    [DllImport(SimpleLibrary, EntryPoint = "pa_simple_write")]
    public static extern int SimpleWrite(IntPtr simple, byte[] data, UIntPtr bytes, out int error);

    [DllImport(Library, EntryPoint = "pa_strerror")]
    public static extern IntPtr StringError(int error);

    [DllImport(Library, EntryPoint = "pa_mainloop_new")]
    public static extern IntPtr MainloopNew();

    [DllImport(Library, EntryPoint = "pa_mainloop_get_api")]
    public static extern IntPtr MainloopGetApi(IntPtr mainloop);

    [DllImport(Library, EntryPoint = "pa_mainloop_iterate")]
    public static extern int MainloopIterate(IntPtr mainloop, int block, IntPtr result);

    [DllImport(Library, EntryPoint = "pa_mainloop_free")]
    public static extern void MainloopFree(IntPtr mainloop);

    [DllImport(Library, EntryPoint = "pa_context_new")]
    public static extern IntPtr ContextNew(IntPtr mainloopApi, string name);

    [DllImport(Library, EntryPoint = "pa_context_connect")]
    public static extern int ContextConnect(IntPtr context, string? server, int flags, IntPtr spawnApi);

    [DllImport(Library, EntryPoint = "pa_context_get_state")]
    public static extern int ContextGetState(IntPtr context);

    [DllImport(Library, EntryPoint = "pa_context_disconnect")]
    public static extern void ContextDisconnect(IntPtr context);

    [DllImport(Library, EntryPoint = "pa_context_unref")]
    public static extern void ContextUnref(IntPtr context);

    [DllImport(Library, EntryPoint = "pa_context_get_source_info_list")]
    public static extern IntPtr ContextGetSourceInfoList(IntPtr context, SourceInfoCallback callback, IntPtr userData);

    [DllImport(Library, EntryPoint = "pa_context_get_server_info")]
    public static extern IntPtr ContextGetServerInfo(IntPtr context, ServerInfoCallback callback, IntPtr userData);

    [DllImport(Library, EntryPoint = "pa_operation_get_state")]
    public static extern int OperationGetState(IntPtr operation);

    [DllImport(Library, EntryPoint = "pa_operation_unref")]
    public static extern void OperationUnref(IntPtr operation);

    /// <summary>
    ///     What an error code of the library means, in English.
    /// </summary>
    public static string Describe(int error) =>
        Marshal.PtrToStringUTF8(StringError(error)) ?? $"error {error}";
}