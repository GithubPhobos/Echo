namespace Echo.Tests.UnitTests;

/// <summary>
///     The talk with the permission windows of a Linux desktop. The tests that need a desktop run against a stand-in
///     on a message bus of their own, so they check what Echo sends and how it reads the answers; whether a real
///     desktop answers the same way is for the check on a live system.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class DesktopPortalTests
{
    /// <summary>
    ///     How long a test waits for what the stand-in desktop sends.
    /// </summary>
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void RequestPath_FollowsTheNameOfTheConnectionAndTheToken()
    {
        // Act & Assert: the colon goes, the dots become underscores
        Assert.Equal("/org/freedesktop/portal/desktop/request/1_42/echooo3", DesktopPortal.RequestPath(":1.42", "echooo3"));
    }

    [Theory]
    [InlineData("VcBackQuote", "", "grave")]
    [InlineData("`", "leftctrl", "CTRL+grave")]
    [InlineData("VcF8", "rightalt", "ALT+F8")]
    [InlineData("VcA", "win", "LOGO+a")]
    public void ToTrigger_WritesTheKeyAsTheShortcutsStandardDoes(string key, string modifier, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, DesktopPortal.ToTrigger(new HotkeyChoice(key, modifier)));
    }

    [Fact]
    public void FindTrigger_ListWithoutThePushToTalkKey_IsNull()
    {
        // Arrange
        var shortcuts = new List<(string, Dictionary<string, VariantValue>)>
        {
            ("other", new Dictionary<string, VariantValue> { ["trigger_description"] = "F1" }),
        };

        // Act & Assert
        Assert.Null(DesktopPortal.FindTrigger(shortcuts));
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task EnsureShortcutAsync_NothingGivenYet_NamesEchoFirstThenAsksForTheSuggestedKey()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(Mock.Of<IUserSettingsStore>(), desktop.Address);

        // Act
        string? label = await portal.EnsureShortcutAsync("CTRL+grave", "Echo: hold to speak", askAgain: false, CancellationToken.None);

        // Assert: the registry comes before anything else, and the desktop's own description comes back
        Assert.Equal("Ctrl + `", label);
        Assert.Equal(DesktopPortal.ApplicationId, desktop.RegisteredId);
        Assert.Equal("org.freedesktop.host.portal.Registry.Register", desktop.Calls[0]);
        Assert.Equal("CTRL+grave", desktop.PreferredTrigger);
        Assert.Contains("org.freedesktop.portal.GlobalShortcuts.BindShortcuts", desktop.Calls);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task EnsureShortcutAsync_DesktopKnowsProgramsByTheirLaunchers_EchoPutsItsOwnInPlaceFirst()
    {
        // Arrange: a desktop that registers only a name it has a launcher for
        string launchers = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

        try
        {
            await using FakeDesktop desktop = await FakeDesktop.StartAsync();
            desktop.LaunchersDirectory = launchers;

            using var portal = new DesktopPortal(Mock.Of<IUserSettingsStore>(), desktop.Address, new DesktopEntry(launchers, "/opt/echooo/echooo"));

            // Act
            string? label = await portal.EnsureShortcutAsync("grave", "Echo: hold to speak", askAgain: false, CancellationToken.None);

            // Assert
            Assert.Equal("Ctrl + `", label);
            Assert.Equal(DesktopPortal.ApplicationId, desktop.RegisteredId);
        }
        finally
        {
            if (Directory.Exists(launchers))
                Directory.Delete(launchers, recursive: true);
        }
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task EnsureShortcutAsync_DesktopRefusesTheName_SaysWhyAndNotJustThatItWantsOne()
    {
        // Arrange: no launcher anywhere
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        desktop.LaunchersDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

        using var portal = new DesktopPortal(Mock.Of<IUserSettingsStore>(), desktop.Address);

        // Act
        var exception = await Assert.ThrowsAsync<DBusErrorReplyException>(
            () => portal.EnsureShortcutAsync("grave", "Echo: hold to speak", askAgain: false, CancellationToken.None));

        // Assert
        Assert.Contains("App info not found", exception.Message);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task EnsureShortcutAsync_KeyGivenInAnEarlierRun_IsFoundWithoutTheWindow()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        desktop.IsBound = true;

        using var portal = new DesktopPortal(Mock.Of<IUserSettingsStore>(), desktop.Address);

        // Act
        string? label = await portal.EnsureShortcutAsync("grave", "Echo: hold to speak", askAgain: false, CancellationToken.None);

        // Assert
        Assert.Equal("Ctrl + `", label);
        Assert.DoesNotContain("org.freedesktop.portal.GlobalShortcuts.BindShortcuts", desktop.Calls);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task EnsureShortcutAsync_KeyPressedAndLetGo_IsReportedForTheKeyOfEcho()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(Mock.Of<IUserSettingsStore>(), desktop.Address);

        var events = new List<string>();
        using var bothArrived = new SemaphoreSlim(0);

        portal.ShortcutActivated += (_, shortcut) => { events.Add($"down {shortcut}"); bothArrived.Release(); };
        portal.ShortcutDeactivated += (_, shortcut) => { events.Add($"up {shortcut}"); bothArrived.Release(); };

        await portal.EnsureShortcutAsync("grave", "Echo: hold to speak", askAgain: false, CancellationToken.None);

        // Act
        desktop.Press(DesktopPortal.SpeakShortcut, isDown: true);
        desktop.Press(DesktopPortal.SpeakShortcut, isDown: false);

        // Assert
        Assert.True(await bothArrived.WaitAsync(_patience));
        Assert.True(await bothArrived.WaitAsync(_patience));
        Assert.Equal(["down speak", "up speak"], events);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task EnsureKeyboardAsync_NoPermissionKeptAndNotAllowedToAsk_AsksNothing()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);

        // Act
        bool mayPress = await portal.EnsureKeyboardAsync(mayAsk: false, CancellationToken.None);

        // Assert: mid-work the desktop is never asked
        Assert.False(mayPress);
        Assert.Empty(desktop.Calls);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task EnsureKeyboardAsync_AskedAndThenRenewedInTheNextRun_KeepsTheNewestPermission()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        var settings = new InMemorySettingsStore();

        // Act: the wizard asks; the next run of Echo renews the permission it kept
        desktop.IssuedToken = "first";
        using (var wizard = new DesktopPortal(settings, desktop.Address))
            Assert.True(await wizard.EnsureKeyboardAsync(mayAsk: true, CancellationToken.None));

        desktop.IssuedToken = "second";
        using var nextRun = new DesktopPortal(settings, desktop.Address);
        bool mayPress = await nextRun.EnsureKeyboardAsync(mayAsk: false, CancellationToken.None);

        // Assert: the keyboard only, kept until revoked, and every start hands out a new permission
        Assert.True(mayPress);
        Assert.Equal((1u, 2u), desktop.SelectedDevices);
        Assert.Equal("first", desktop.ReceivedToken);
        Assert.Equal("second", settings.ReadValue(DesktopPortal.SettingsSection, DesktopPortal.TokenKey));
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task PressAsync_ShiftInsert_PressesBothAndLetsThemGoInReverseOrder()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);

        await portal.EnsureKeyboardAsync(mayAsk: true, CancellationToken.None);

        // Act
        bool isPressed = await portal.PressAsync(0xffe1, 0xff63);

        // Assert
        Assert.True(isPressed);
        Assert.Equal([(0xffe1, 1u), (0xff63, 1u), (0xff63, 0u), (0xffe1, 0u)], desktop.Keys);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task OpenFilesAsync_FilePicked_GivesItsPathAndSendsThePatterns()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);

        // Act
        IReadOnlyList<string> files = await portal.OpenFilesAsync("Pick a model", ["*.bin"], CancellationToken.None);

        // Assert
        Assert.Equal(["/home/someone/ggml-small.bin"], files);
        Assert.Equal(["*.bin"], desktop.Patterns);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task VersionAsync_PortalTheDesktopLacks_IsNull()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);

        // Act & Assert
        Assert.Equal(1u, await portal.VersionAsync(DesktopPortal.ShortcutsInterface));
        Assert.Null(await portal.VersionAsync("org.freedesktop.portal.Nothing"));
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task EnsureShortcutAsync_StoppedWhileTheWindowIsOpen_StopsWaitingAndClosesTheWindow()
    {
        // Arrange: the user has not dealt with the window that gives the key
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        desktop.HoldsBind = true;

        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);
        using var stopping = new CancellationTokenSource();

        Task<string?> asking = portal.EnsureShortcutAsync("grave", "Echo: hold to speak", askAgain: false, stopping.Token);
        await desktop.BindShown.Task.WaitAsync(_patience);

        // Act
        stopping.Cancel();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking.WaitAsync(_patience));
        Assert.True(SpinWait.SpinUntil(() => desktop.CountOf("org.freedesktop.portal.Request.Close") == 1, _patience));
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task EnsureShortcutAsync_AskedAgainInTheSameRun_TakesTheKeyInANewSession()
    {
        // Arrange: the wizard asked once, the working screen ran, and the wizard asks again; a session takes its
        // keys once
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);

        await portal.EnsureShortcutAsync("grave", "Echo: hold to speak", askAgain: true, CancellationToken.None);
        string? first = desktop.ShortcutsSession;

        // Act
        string? label = await portal.EnsureShortcutAsync("F8", "Echo: hold to speak", askAgain: true, CancellationToken.None);

        // Assert
        Assert.Equal("Ctrl + `", label);
        Assert.Equal("F8", desktop.PreferredTrigger);
        Assert.NotEqual(first, desktop.ShortcutsSession);
        Assert.True(SpinWait.SpinUntil(() => desktop.CountOf("org.freedesktop.portal.Session.Close") == 1, _patience));
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task ShortcutsLost_DesktopClosesTheSessionOfTheKey_IsReported()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);

        await portal.EnsureShortcutAsync("grave", "Echo: hold to speak", askAgain: false, CancellationToken.None);

        var lost = new TaskCompletionSource<ShortcutsLostEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        portal.ShortcutsLost += (_, e) => lost.TrySetResult(e);

        // Act: as when the user takes the key back
        desktop.CloseSession(desktop.ShortcutsSession!);

        // Assert: the session went, not the portal service
        ShortcutsLostEventArgs loss = await lost.Task.WaitAsync(_patience);
        Assert.Contains("closed", loss.Reason);
        Assert.False(loss.IsServiceGone);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task RestoreShortcutAsync_PortalServiceRestarted_NamesEchoAgainAndFindsTheKeyWithoutTheWindow()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);

        await portal.EnsureShortcutAsync("grave", "Echo: hold to speak", askAgain: false, CancellationToken.None);

        var lost = new TaskCompletionSource<ShortcutsLostEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        portal.ShortcutsLost += (_, e) => lost.TrySetResult(e);

        // Act
        await desktop.RestartAsync();
        string? label = await portal.RestoreShortcutAsync(CancellationToken.None);

        // Assert: the new service knows Echo, the key the desktop kept is found again, and its loss was reported
        Assert.Equal(DesktopPortal.ApplicationId, desktop.RegisteredId);
        Assert.Equal("Ctrl + `", label);
        Assert.Equal(1, desktop.CountOf("org.freedesktop.portal.GlobalShortcuts.BindShortcuts"));
        ShortcutsLostEventArgs loss = await lost.Task.WaitAsync(_patience);
        Assert.Contains("portal service", loss.Reason);
        Assert.True(loss.IsServiceGone);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task ShortcutsLost_PortalServiceStopsAndNoneTakesItsPlace_IsReportedOnceWithoutAnyCall()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);

        await portal.EnsureShortcutAsync("grave", "Echo: hold to speak", askAgain: false, CancellationToken.None);

        int losses = 0;
        var lost = new TaskCompletionSource<ShortcutsLostEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);

        portal.ShortcutsLost += (_, e) =>
        {
            Interlocked.Increment(ref losses);
            lost.TrySetResult(e);
        };

        // Act: Echo calls nothing meanwhile, so nothing would start a new service
        desktop.StopService();

        // Assert: the loss is reported while no service runs at all
        ShortcutsLostEventArgs loss = await lost.Task.WaitAsync(_patience);
        Assert.Contains("stopped", loss.Reason);
        Assert.True(loss.IsServiceGone);

        // A service that comes later is not a second loss, and the key is found there again
        await desktop.StartServiceAsync();
        string? label = await portal.RestoreShortcutAsync(CancellationToken.None);

        Assert.Equal("Ctrl + `", label);
        Assert.Equal(1, Volatile.Read(ref losses));
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task CloseShortcutsAsync_KeyGivenBack_ClosesItsSessionAndTheKeyIsFoundAgainWithoutTheWindow()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        desktop.IsBound = true;

        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);

        await portal.EnsureShortcutAsync("grave", "Echo: hold to speak", askAgain: false, CancellationToken.None);
        string first = desktop.ShortcutsSession!;

        // Act
        await portal.CloseShortcutsAsync();

        // Assert: the desktop is told to close the session; asking again, as the setup wizard does, takes the key in a new one
        Assert.True(SpinWait.SpinUntil(() => desktop.CountOf("org.freedesktop.portal.Session.Close") == 1, _patience));

        string? label = await portal.EnsureShortcutAsync("grave", "Echo: hold to speak", askAgain: false, CancellationToken.None);

        Assert.Equal("Ctrl + `", label);
        Assert.NotEqual(first, desktop.ShortcutsSession);
        Assert.Equal(0, desktop.CountOf("org.freedesktop.portal.GlobalShortcuts.BindShortcuts"));
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task VersionAsync_PortalServiceRestarted_NamesEchoToTheNewOne()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);

        await portal.VersionAsync(DesktopPortal.ShortcutsInterface);

        // Act
        await desktop.RestartAsync();
        await portal.VersionAsync(DesktopPortal.ShortcutsInterface);

        // Assert
        Assert.Equal(DesktopPortal.ApplicationId, desktop.RegisteredId);
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task EnsureKeyboardAsync_PermissionTakenBackMidWork_IsNotAskedForAgainUntilTheWizardAsks()
    {
        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new InMemorySettingsStore(), desktop.Address);

        Assert.True(await portal.EnsureKeyboardAsync(mayAsk: true, CancellationToken.None));

        // Act: the desktop closes the session, as when the user takes the permission back
        desktop.CloseSession(desktop.KeyboardSession!);

        // Assert: the next phrase only waits for the session to be gone; the desktop is not asked, so no window opens
        Assert.True(SpinWait.SpinUntil(
            () => !portal.EnsureKeyboardAsync(mayAsk: false, CancellationToken.None).GetAwaiter().GetResult(),
            _patience));
        Assert.Equal(1, desktop.CountOf("org.freedesktop.portal.RemoteDesktop.Start"));

        // The wizard asks again, and from then on Echo may press keys
        Assert.True(await portal.EnsureKeyboardAsync(mayAsk: true, CancellationToken.None));
        Assert.True(await portal.EnsureKeyboardAsync(mayAsk: false, CancellationToken.None));
        Assert.Equal(2, desktop.CountOf("org.freedesktop.portal.RemoteDesktop.Start"));
    }
}