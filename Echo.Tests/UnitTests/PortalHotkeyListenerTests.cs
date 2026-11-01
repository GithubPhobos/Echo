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

        var settings = new InMemorySettingsStore();
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

        var settings = new InMemorySettingsStore();
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

        var settings = new InMemorySettingsStore();
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

        var settings = new InMemorySettingsStore();
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

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task Lost_PortalServiceStopsAndTheKeyDoesNotComeBack_SaysToLogInAgain()
    {
        // Arrange: a key given in an earlier run, which the next service does not give again, as on GNOME
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        desktop.IsBound = true;

        var settings = new InMemorySettingsStore();
        using var portal = new DesktopPortal(settings, desktop.Address);
        var warnings = new Warnings();
        using PortalHotkeyListener listener = Listener(portal, settings, warnings.Logger);

        await listener.StartAsync(CancellationToken.None);

        // Act
        desktop.IsBound = false;
        desktop.StopService();
        await desktop.StartServiceAsync();

        // Assert: after looking for it a few times, Echo says what brings the key back, which /setup would not
        string advice = await warnings.GivenUp.Task.WaitAsync(_patience * 2);
        Assert.Contains("Log out and log in again", advice);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task Lost_DesktopClosesTheSessionAndTheKeyDoesNotComeBack_SaysToAskWithSetup()
    {
        // Arrange: the user takes the key back in the settings of the desktop
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        desktop.IsBound = true;

        var settings = new InMemorySettingsStore();
        using var portal = new DesktopPortal(settings, desktop.Address);
        var warnings = new Warnings();
        using PortalHotkeyListener listener = Listener(portal, settings, warnings.Logger);

        await listener.StartAsync(CancellationToken.None);

        // Act
        desktop.IsBound = false;
        desktop.CloseSession(desktop.ShortcutsSession!);

        // Assert
        string advice = await warnings.GivenUp.Task.WaitAsync(_patience * 2);
        Assert.Contains("/setup", advice);
    }

    private static PortalHotkeyListener Listener(DesktopPortal portal, IUserSettingsStore settings, ILogger<PortalHotkeyListener>? logger = null)
    {
        var listener = new PortalHotkeyListener(portal, logger ?? Mock.Of<ILogger<PortalHotkeyListener>>(), settings);
        listener.Bind(HotkeyChoice.Default);

        return listener;
    }

    /// <summary>
    ///     The warning the listener gives up with once the key does not come back.
    /// </summary>
    private sealed class Warnings
    {
        private readonly Mock<ILogger<PortalHotkeyListener>> _logger = new();

        public Warnings()
        {
            _logger
                .Setup(logger => logger.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((value, _) => value.ToString()!.Contains("no longer")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(call => GivenUp.TrySetResult(call.Arguments[2].ToString()!)));
        }

        public ILogger<PortalHotkeyListener> Logger => _logger.Object;

        public TaskCompletionSource<string> GivenUp { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}