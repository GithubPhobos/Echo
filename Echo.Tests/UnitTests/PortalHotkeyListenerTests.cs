using System.Runtime.Versioning;
using Echo.Platform.Linux;
using Echo.Setup.Hotkeys;

namespace Echo.Tests.UnitTests;

/// <summary>
///     The push-to-talk key on Wayland, given by a stand-in desktop on a message bus of its own.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class PortalHotkeyListenerTests
{
    /// <summary>
    ///     How long a test waits for what the stand-in desktop sends; looking for a lost key again takes a second.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task StartAsync_EchoStopsWhileTheDesktopAsksForTheKey_StopsWaitingAndListensToNothing()
    {
        // Arrange: no key given yet, and the user leaves the window open
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        desktop.HoldsBind = true;

        var settings = new MemorySettings();
        using var portal = new DesktopPortal(settings, desktop.Address);
        using PortalHotkeyListener listener = Listener(portal, settings);
        using var stopping = new CancellationTokenSource();

        int presses = 0;
        listener.Pressed += (_, _) => Interlocked.Increment(ref presses);

        Task starting = listener.StartAsync(stopping.Token);
        await desktop.BindShown.Task.WaitAsync(_patience);

        // Act
        stopping.Cancel();

        // Assert: the window is closed, and a press after that reaches nobody
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting.WaitAsync(_patience));
        Assert.True(SpinWait.SpinUntil(() => desktop.CountOf("org.freedesktop.portal.Request.Close") == 1, _patience));

        desktop.Press(DesktopPortal.SpeakShortcut, isDown: true);
        await portal.VersionAsync(DesktopPortal.ShortcutsInterface);

        Assert.Equal(0, Volatile.Read(ref presses));
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task Lost_DesktopClosesTheSessionWhileTheKeyIsHeld_IsReportedAndTheKeyComesBack()
    {
        // Arrange: a key given in an earlier run
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        desktop.IsBound = true;

        var settings = new MemorySettings();
        using var portal = new DesktopPortal(settings, desktop.Address);
        using PortalHotkeyListener listener = Listener(portal, settings);

        using var pressed = new SemaphoreSlim(0);
        using var lost = new SemaphoreSlim(0);
        listener.Pressed += (_, _) => pressed.Release();
        listener.Lost += (_, _) => lost.Release();

        await listener.StartAsync(CancellationToken.None);

        desktop.Press(DesktopPortal.SpeakShortcut, isDown: true);
        Assert.True(await pressed.WaitAsync(_patience));

        string first = desktop.ShortcutsSession!;

        // Act: the session ends while the key is down, so its "up" never comes
        desktop.CloseSession(first);

        // Assert: the loss is reported, and the key works again in a new session, without the window; Echo has the
        // new session once it asks which keys it has there
        Assert.True(await lost.WaitAsync(_patience));
        Assert.True(SpinWait.SpinUntil(() => desktop.CountOf("org.freedesktop.portal.GlobalShortcuts.ListShortcuts") == 2, _patience));
        Assert.NotEqual(first, desktop.ShortcutsSession);

        desktop.Press(DesktopPortal.SpeakShortcut, isDown: true);

        Assert.True(await pressed.WaitAsync(_patience));
        Assert.Equal(0, desktop.CountOf("org.freedesktop.portal.GlobalShortcuts.BindShortcuts"));
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task Lost_PortalServiceStopsWhileTheKeyIsHeld_IsReportedWithoutWaitingForANewOne()
    {
        // Arrange: a key given in an earlier run
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        desktop.IsBound = true;

        var settings = new MemorySettings();
        using var portal = new DesktopPortal(settings, desktop.Address);
        using PortalHotkeyListener listener = Listener(portal, settings);

        using var pressed = new SemaphoreSlim(0);
        using var lost = new SemaphoreSlim(0);
        listener.Pressed += (_, _) => pressed.Release();
        listener.Lost += (_, _) => lost.Release();

        await listener.StartAsync(CancellationToken.None);

        desktop.Press(DesktopPortal.SpeakShortcut, isDown: true);
        Assert.True(await pressed.WaitAsync(_patience));

        // Act: the service ends while the key is down, so its "up" never comes, and no new service runs yet
        desktop.StopService();

        // Assert: the loss is reported at once; a service that comes later gives the key in a new session, and is
        // not a second loss
        Assert.True(await lost.WaitAsync(_patience));

        await desktop.StartServiceAsync();
        Assert.True(SpinWait.SpinUntil(() => desktop.CountOf("org.freedesktop.portal.GlobalShortcuts.ListShortcuts") == 2, _patience));

        desktop.Press(DesktopPortal.SpeakShortcut, isDown: true);

        Assert.True(await pressed.WaitAsync(_patience));
        Assert.Equal(0, lost.CurrentCount);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task ReleaseAsync_EchoCouldNotStart_ClosesTheSessionOfTheKeyAndHearsItNoMore()
    {
        // Arrange: a key given in an earlier run
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        desktop.IsBound = true;

        var settings = new MemorySettings();
        using var portal = new DesktopPortal(settings, desktop.Address);
        using PortalHotkeyListener listener = Listener(portal, settings);

        int presses = 0;
        listener.Pressed += (_, _) => Interlocked.Increment(ref presses);

        await listener.StartAsync(CancellationToken.None);

        // Act
        await listener.ReleaseAsync();

        // Assert: the desktop is told to close the session, so it gives the key to other programs again
        Assert.True(SpinWait.SpinUntil(() => desktop.CountOf("org.freedesktop.portal.Session.Close") == 1, _patience));

        desktop.Press(DesktopPortal.SpeakShortcut, isDown: true);
        await portal.VersionAsync(DesktopPortal.ShortcutsInterface);

        Assert.Equal(0, Volatile.Read(ref presses));
    }

    private static PortalHotkeyListener Listener(DesktopPortal portal, IUserSettingsStore settings)
    {
        var listener = new PortalHotkeyListener(portal, Mock.Of<ILogger<PortalHotkeyListener>>(), settings);
        listener.Bind(HotkeyChoice.Default);

        return listener;
    }
}