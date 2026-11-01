using System.Runtime.InteropServices;

namespace Echo.Tests.UnitTests;

/// <summary>
///     The notices of Windows about the session, sent the way Windows sends them: as a message to the hidden window
///     .NET opens for them in the process.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSessionEventsTests
{
    /// <summary>
    ///     WM_WTSSESSION_CHANGE and the reasons it comes with.
    /// </summary>
    private const uint SessionChange = 0x02B1;
    private const int ConsoleConnect = 0x1;
    private const int RemoteConnect = 0x3;
    private const int SessionLock = 0x7;
    private const int SessionUnlock = 0x8;

    /// <summary>
    ///     How long a test waits for a notice, and for the window that takes it.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private delegate bool WindowVisitor(IntPtr window, IntPtr parameter);

    [WindowsFact]
    public void Returned_ScreenOrRemoteConnectionTakesTheSessionAgain_IsRaisedForEach()
    {
        // Arrange
        using var events = new WindowsSessionEvents();
        using var returns = new SemaphoreSlim(0);
        events.Returned += (_, _) => returns.Release();

        IntPtr window = NoticeWindow();

        // Act
        Notify(window, ConsoleConnect);
        Notify(window, RemoteConnect);

        // Assert
        Assert.True(returns.Wait(_patience));
        Assert.True(returns.Wait(_patience));
    }

    [WindowsFact]
    public void Returned_ScreenLockedAndUnlocked_IsNotRaised()
    {
        // Arrange
        using var events = new WindowsSessionEvents();
        int returns = 0;
        using var returned = new ManualResetEventSlim();

        events.Returned += (_, _) =>
        {
            Interlocked.Increment(ref returns);
            returned.Set();
        };

        IntPtr window = NoticeWindow();

        // Act: the notices come in order, so once the return after them is told, they were handled
        Notify(window, SessionLock);
        Notify(window, SessionUnlock);
        Notify(window, ConsoleConnect);

        // Assert: locking keeps the session and its sound
        Assert.True(returned.Wait(_patience));
        Assert.Equal(1, Volatile.Read(ref returns));
    }

    [WindowsFact]
    public void Dispose_ReturnAfterwards_IsNotRaised()
    {
        // Arrange: another listener that hears the same notice says when it was handled
        using var witness = new WindowsSessionEvents();
        using var witnessed = new ManualResetEventSlim();
        witness.Returned += (_, _) => witnessed.Set();

        var events = new WindowsSessionEvents();
        int returns = 0;
        events.Returned += (_, _) => Interlocked.Increment(ref returns);

        IntPtr window = NoticeWindow();

        // Act
        events.Dispose();
        Notify(window, ConsoleConnect);

        // Assert
        Assert.True(witnessed.Wait(_patience));
        Assert.Equal(0, Volatile.Read(ref returns));
    }

    /// <summary>
    ///     The hidden window of this process that takes the notices of the system for .NET.
    /// </summary>
    private static IntPtr NoticeWindow()
    {
        IntPtr found = IntPtr.Zero;

        Assert.True(SpinWait.SpinUntil(() => (found = FindNoticeWindow()) != IntPtr.Zero, _patience));

        return found;
    }

    private static IntPtr FindNoticeWindow()
    {
        IntPtr found = IntPtr.Zero;
        var name = new StringBuilder(256);

        EnumWindows(
            (window, _) =>
            {
                GetWindowThreadProcessId(window, out uint processId);

                if (processId != (uint)Environment.ProcessId)
                    return true;

                name.Clear();
                GetClassName(window, name, name.Capacity);

                if (!name.ToString().StartsWith(".NET-BroadcastEventWindow", StringComparison.Ordinal))
                    return true;

                found = window;

                return false;
            },
            IntPtr.Zero);

        return found;
    }

    private static void Notify(IntPtr window, int reason) =>
        Assert.True(PostMessage(window, SessionChange, reason, Process.GetCurrentProcess().SessionId));

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowVisitor visitor, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int size);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}