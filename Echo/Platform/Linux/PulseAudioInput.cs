namespace Echo.Platform.Linux;

/// <summary>
///     The microphones of Linux, as the sound server lists them, under the names the desktop shows in its own
///     sound settings. The microphone the desktop uses by default comes first, so the first one is the one
///     a fresh setup picks.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class PulseAudioInput : IAudioInput
{
    /// <summary>
    ///     Asking the sound server takes a few round trips; one that does not answer is not waited for longer.
    /// </summary>
    private static readonly TimeSpan _listTimeout = TimeSpan.FromSeconds(3);

    /// <inheritdoc/>
    public IReadOnlyList<MicrophoneDevice> GetDevices() =>
        ListSources()
            .Select((source, index) => new MicrophoneDevice(index, source.Description))
            .ToList();

    /// <inheritdoc/>
    public IAudioInputStream Open(int deviceIndex, int bufferMilliseconds, int numberOfBuffers)
    {
        IReadOnlyList<Source> sources = ListSources();

        // A device that is gone since setup leaves the choice to the sound server: its default microphone
        string? name = deviceIndex >= 0 && deviceIndex < sources.Count ? sources[deviceIndex].Name : null;

        return new PulseAudioInputStream(name, bufferMilliseconds);
    }

    /// <summary>
    ///     The recording devices of the sound server, the default one first. The "monitors" that record what
    ///     the loudspeakers play are left out: they are no microphones.
    /// </summary>
    private static IReadOnlyList<Source> ListSources()
    {
        var sources = new List<Source>();
        string? defaultSource = null;

        IntPtr mainloop = PulseAudioNative.MainloopNew();
        IntPtr context = PulseAudioNative.ContextNew(PulseAudioNative.MainloopGetApi(mainloop), ApplicationInfo.CommandName);

        // Kept in variables, so the collector does not take them away while the library still calls them
        PulseAudioNative.ServerInfoCallback onServer = (_, info, _) =>
        {
            if (info != IntPtr.Zero)
                defaultSource = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, PulseAudioNative.DefaultSourceOffset));
        };

        PulseAudioNative.SourceInfoCallback onSource = (_, info, isLast, _) =>
        {
            if (isLast != 0 || info == IntPtr.Zero)
                return;

            if ((uint)Marshal.ReadInt32(info, PulseAudioNative.MonitorOfSinkOffset) != PulseAudioNative.Invalid)
                return;

            string? name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info));
            string? description = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, 2 * IntPtr.Size));

            if (name is not null)
                sources.Add(new Source(name, string.IsNullOrWhiteSpace(description) ? name : description));
        };

        try
        {
            var deadline = DateTime.UtcNow + _listTimeout;

            if (PulseAudioNative.ContextConnect(context, null, PulseAudioNative.NoAutospawn, IntPtr.Zero) < 0
                || !RunUntil(mainloop, () => PulseAudioNative.ContextGetState(context) is PulseAudioNative.ContextReady
                                                 or PulseAudioNative.ContextFailed
                                                 or PulseAudioNative.ContextTerminated, deadline)
                || PulseAudioNative.ContextGetState(context) != PulseAudioNative.ContextReady)
            {
                return [];
            }

            Wait(mainloop, PulseAudioNative.ContextGetServerInfo(context, onServer, IntPtr.Zero), deadline);
            Wait(mainloop, PulseAudioNative.ContextGetSourceInfoList(context, onSource, IntPtr.Zero), deadline);

            PulseAudioNative.ContextDisconnect(context);
        }
        finally
        {
            PulseAudioNative.ContextUnref(context);
            PulseAudioNative.MainloopFree(mainloop);

            GC.KeepAlive(onServer);
            GC.KeepAlive(onSource);
        }

        return [.. sources.OrderByDescending(source => source.Name == defaultSource)];
    }

    /// <summary>
    ///     Runs the library until an operation is over.
    /// </summary>
    private static void Wait(IntPtr mainloop, IntPtr operation, DateTime deadline)
    {
        if (operation == IntPtr.Zero)
            return;

        RunUntil(mainloop, () => PulseAudioNative.OperationGetState(operation) != PulseAudioNative.OperationRunning, deadline);
        PulseAudioNative.OperationUnref(operation);
    }

    /// <summary>
    ///     Runs the library, one event at a time, until <paramref name="isDone"/> or the deadline.
    /// </summary>
    private static bool RunUntil(IntPtr mainloop, Func<bool> isDone, DateTime deadline)
    {
        while (!isDone())
        {
            // Without blocking, so a server that stays silent cannot hold the wizard past the deadline
            if (PulseAudioNative.MainloopIterate(mainloop, 0, IntPtr.Zero) < 0 || DateTime.UtcNow > deadline)
                return false;

            Thread.Sleep(1);
        }

        return true;
    }

    /// <summary>
    ///     A recording device: its name for the sound server and the one the user sees.
    /// </summary>
    private sealed record Source(string Name, string Description);
}