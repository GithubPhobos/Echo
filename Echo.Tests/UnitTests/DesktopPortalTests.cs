using System.Diagnostics;
using System.Runtime.Versioning;
using Echo.Platform.Linux;
using Echo.Setup.Hotkeys;
using Tmds.DBus.Protocol;

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

    [Fact]
    public async Task EnsureShortcutAsync_NothingGivenYet_NamesEchoFirstThenAsksForTheSuggestedKey()
    {
        if (!OperatingSystem.IsLinux() || !FakeDesktop.CanRun)
            return;

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

    [Fact]
    public async Task EnsureShortcutAsync_DesktopKnowsProgramsByTheirLaunchers_EchoPutsItsOwnInPlaceFirst()
    {
        if (!OperatingSystem.IsLinux() || !FakeDesktop.CanRun)
            return;

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

    [Fact]
    public async Task EnsureShortcutAsync_DesktopRefusesTheName_SaysWhyAndNotJustThatItWantsOne()
    {
        if (!OperatingSystem.IsLinux() || !FakeDesktop.CanRun)
            return;

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

    [Fact]
    public async Task EnsureShortcutAsync_KeyGivenInAnEarlierRun_IsFoundWithoutTheWindow()
    {
        if (!OperatingSystem.IsLinux() || !FakeDesktop.CanRun)
            return;

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

    [Fact]
    public async Task EnsureShortcutAsync_KeyPressedAndLetGo_IsReportedForTheKeyOfEcho()
    {
        if (!OperatingSystem.IsLinux() || !FakeDesktop.CanRun)
            return;

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

    [Fact]
    public async Task EnsureKeyboardAsync_NoPermissionKeptAndNotAllowedToAsk_AsksNothing()
    {
        if (!OperatingSystem.IsLinux() || !FakeDesktop.CanRun)
            return;

        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new MemorySettings(), desktop.Address);

        // Act
        bool mayPress = await portal.EnsureKeyboardAsync(mayAsk: false, CancellationToken.None);

        // Assert: mid-work the desktop is never asked
        Assert.False(mayPress);
        Assert.Empty(desktop.Calls);
    }

    [Fact]
    public async Task EnsureKeyboardAsync_AskedAndThenRenewedInTheNextRun_KeepsTheNewestPermission()
    {
        if (!OperatingSystem.IsLinux() || !FakeDesktop.CanRun)
            return;

        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        var settings = new MemorySettings();

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

    [Fact]
    public async Task PressAsync_ShiftInsert_PressesBothAndLetsThemGoInReverseOrder()
    {
        if (!OperatingSystem.IsLinux() || !FakeDesktop.CanRun)
            return;

        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new MemorySettings(), desktop.Address);

        await portal.EnsureKeyboardAsync(mayAsk: true, CancellationToken.None);

        // Act
        bool isPressed = await portal.PressAsync(0xffe1, 0xff63);

        // Assert
        Assert.True(isPressed);
        Assert.Equal([(0xffe1, 1u), (0xff63, 1u), (0xff63, 0u), (0xffe1, 0u)], desktop.Keys);
    }

    [Fact]
    public async Task OpenFilesAsync_FilePicked_GivesItsPathAndSendsThePatterns()
    {
        if (!OperatingSystem.IsLinux() || !FakeDesktop.CanRun)
            return;

        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new MemorySettings(), desktop.Address);

        // Act
        IReadOnlyList<string> files = await portal.OpenFilesAsync("Pick a model", ["*.bin"], CancellationToken.None);

        // Assert
        Assert.Equal(["/home/someone/ggml-small.bin"], files);
        Assert.Equal(["*.bin"], desktop.Patterns);
    }

    [Fact]
    public async Task VersionAsync_PortalTheDesktopLacks_IsNull()
    {
        if (!OperatingSystem.IsLinux() || !FakeDesktop.CanRun)
            return;

        // Arrange
        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new MemorySettings(), desktop.Address);

        // Act & Assert
        Assert.Equal(1u, await portal.VersionAsync(DesktopPortal.ShortcutsInterface));
        Assert.Null(await portal.VersionAsync("org.freedesktop.portal.Nothing"));
    }

    /// <summary>
    ///     A settings file kept in memory.
    /// </summary>
    private sealed class MemorySettings : IUserSettingsStore
    {
        private readonly Dictionary<string, string?> _values = [];

        public UserSettings Load() => new();

        public void Save(UserSettings settings)
        {
        }

        public string? ReadValue(string section, string key) =>
            _values.GetValueOrDefault($"{section}:{key}");

        public void WriteValue(string section, string key, string? value) =>
            _values[$"{section}:{key}"] = value;
    }

    /// <summary>
    ///     A desktop that answers like the XDG portals do, on a message bus started for the test alone.
    /// </summary>
    private sealed class FakeDesktop : IPathMethodHandler, IAsyncDisposable
    {
        private const string Service = "org.freedesktop.portal.Desktop";
        private const string ObjectPath = "/org/freedesktop/portal/desktop";
        private const string BusProgram = "/usr/bin/dbus-daemon";

        private readonly Process _bus;
        private readonly DBusConnection _connection;
        private readonly object _lock = new();
        private string? _shortcutsSession;
        private string? _client;

        private FakeDesktop(Process bus, string address, DBusConnection connection)
        {
            _bus = bus;
            _connection = connection;
            Address = address;
        }

        public static bool CanRun => File.Exists(BusProgram);

        public string Address { get; }

        public string Path => ObjectPath;

        public bool HandlesChildPaths => false;

        public List<string> Calls { get; } = [];

        public string? RegisteredId { get; private set; }

        /// <summary>
        ///     When set, the desktop registers only a name it has a launcher for in this folder, and opens no
        ///     session for a program it could not register, the way GNOME 50 on Ubuntu 26.04 does.
        /// </summary>
        public string? LaunchersDirectory { get; set; }

        public string? PreferredTrigger { get; private set; }

        public bool IsBound { get; set; }

        public string IssuedToken { get; set; } = "token";

        public string? ReceivedToken { get; private set; }

        public (uint Types, uint Persist) SelectedDevices { get; private set; }

        public List<(int Keysym, uint State)> Keys { get; } = [];

        public List<string> Patterns { get; } = [];

        public static async Task<FakeDesktop> StartAsync()
        {
            var bus = Process.Start(new ProcessStartInfo(BusProgram, "--session --nofork --print-address")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            })!;

            string address = (await bus.StandardOutput.ReadLineAsync())!.Trim();

            var connection = new DBusConnection(address);
            await connection.ConnectAsync();
            await connection.RequestNameAsync(Service, RequestNameOptions.Default);

            var desktop = new FakeDesktop(bus, address, connection);
            connection.AddMethodHandler(desktop);

            return desktop;
        }

        /// <summary>
        ///     The user presses or lets go of a key the desktop gave.
        /// </summary>
        public void Press(string shortcut, bool isDown)
        {
            using MessageWriter writer = _connection.GetMessageWriter();

            writer.WriteSignalHeader(_client, ObjectPath, DesktopPortal.ShortcutsInterface, isDown ? "Activated" : "Deactivated", "osta{sv}");
            writer.WriteObjectPath(_shortcutsSession!);
            writer.WriteString(shortcut);
            writer.WriteUInt64(1);
            writer.WriteDictionary(new Dictionary<string, VariantValue>());

            _connection.TrySendMessage(writer.CreateMessage());
        }

        public ValueTask HandleMethodAsync(MethodContext context)
        {
            Message request = context.Request;
            string member = request.MemberAsString ?? string.Empty;
            string portalInterface = request.InterfaceAsString ?? string.Empty;

            lock (_lock)
            {
                if (portalInterface != "org.freedesktop.DBus.Properties")
                    Calls.Add($"{portalInterface}.{member}");
            }

            _client = request.SenderAsString;

            Reader reader = request.GetBodyReader();

            switch (member)
            {
                case "Register":
                    string id = reader.ReadString();

                    if (LaunchersDirectory is not null && !File.Exists(System.IO.Path.Combine(LaunchersDirectory, id + ".desktop")))
                    {
                        context.ReplyError("org.freedesktop.portal.Error.Failed", $"Could not register app ID: App info not found for '{id}'");

                        break;
                    }

                    RegisteredId = id;
                    ReplyEmpty(context);

                    break;

                case "Get":
                    string asked = reader.ReadString();

                    if (asked.StartsWith("org.freedesktop.portal.Nothing", StringComparison.Ordinal))
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", "No such interface");

                        break;
                    }

                    using (MessageWriter reply = context.CreateReplyWriter("v"))
                    {
                        reply.WriteVariantUInt32(1);
                        context.Reply(reply.CreateMessage());
                    }

                    break;

                case "CreateSession":
                    if (LaunchersDirectory is not null && RegisteredId is null)
                    {
                        context.ReplyError("org.freedesktop.portal.Error.NotAllowed", "An app id is required");

                        break;
                    }

                    Dictionary<string, VariantValue> options = reader.ReadDictionaryOfStringToVariantValue();
                    string session = $"{ObjectPath}/session/{Sender(request)}/{options["session_handle_token"].GetString()}";

                    if (portalInterface == DesktopPortal.ShortcutsInterface)
                        _shortcutsSession = session;

                    Respond(context, request, options, new Dictionary<string, VariantValue> { ["session_handle"] = session });

                    break;

                case "ListShortcuts":
                    reader.ReadObjectPath();
                    Respond(context, request, reader.ReadDictionaryOfStringToVariantValue(), Shortcuts(IsBound));

                    break;

                case "BindShortcuts":
                    reader.ReadObjectPath();

                    ArrayEnd end = reader.ReadArrayStart(DBusType.Struct);
                    while (reader.HasNext(end))
                    {
                        reader.AlignStruct();
                        reader.ReadString();
                        PreferredTrigger = reader.ReadDictionaryOfStringToVariantValue()["preferred_trigger"].GetString();
                    }

                    reader.ReadString();
                    IsBound = true;
                    Respond(context, request, reader.ReadDictionaryOfStringToVariantValue(), Shortcuts(isBound: true));

                    break;

                case "SelectDevices":
                    reader.ReadObjectPath();
                    Dictionary<string, VariantValue> selection = reader.ReadDictionaryOfStringToVariantValue();

                    SelectedDevices = (selection["types"].GetUInt32(), selection["persist_mode"].GetUInt32());
                    ReceivedToken = selection.TryGetValue("restore_token", out VariantValue token) ? token.GetString() : null;
                    Respond(context, request, selection, new Dictionary<string, VariantValue>());

                    break;

                case "Start":
                    reader.ReadObjectPath();
                    reader.ReadString();
                    Respond(context, request, reader.ReadDictionaryOfStringToVariantValue(), new Dictionary<string, VariantValue>
                    {
                        ["devices"] = 1u,
                        ["restore_token"] = IssuedToken,
                    });

                    break;

                case "NotifyKeyboardKeysym":
                    reader.ReadObjectPath();
                    reader.ReadDictionaryOfStringToVariantValue();

                    lock (_lock)
                        Keys.Add((reader.ReadInt32(), reader.ReadUInt32()));

                    ReplyEmpty(context);

                    break;

                case "OpenFile":
                    reader.ReadString();
                    reader.ReadString();
                    Dictionary<string, VariantValue> chooser = reader.ReadDictionaryOfStringToVariantValue();

                    VariantValue globs = chooser["filters"].GetItem(0).GetItem(1);
                    for (int index = 0; index < globs.Count; index++)
                        Patterns.Add(globs.GetItem(index).GetItem(1).GetString());

                    Respond(context, request, chooser, new Dictionary<string, VariantValue>
                    {
                        ["uris"] = new Tmds.DBus.Protocol.Array<string>(["file:///home/someone/ggml-small.bin"]),
                    });

                    break;

                default:
                    context.ReplyUnknownMethodError();

                    break;
            }

            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            _connection.Dispose();

            _bus.Kill();
            await _bus.WaitForExitAsync();
            _bus.Dispose();
        }

        private static string Sender(Message request) =>
            (request.SenderAsString ?? string.Empty).TrimStart(':').Replace('.', '_');

        /// <summary>
        ///     Answers a request the way the portals do: the call returns the path of the request at once, and
        ///     the result follows as a signal on that path.
        /// </summary>
        private void Respond(MethodContext context, Message request, Dictionary<string, VariantValue> options, Dictionary<string, VariantValue> results)
        {
            string requestPath = DesktopPortal.RequestPath(request.SenderAsString, options["handle_token"].GetString());
            string? sender = request.SenderAsString;

            using (MessageWriter reply = context.CreateReplyWriter("o"))
            {
                reply.WriteObjectPath(requestPath);
                context.Reply(reply.CreateMessage());
            }

            using MessageWriter writer = _connection.GetMessageWriter();

            writer.WriteSignalHeader(sender, requestPath, "org.freedesktop.portal.Request", "Response", "ua{sv}");
            writer.WriteUInt32(0);
            writer.WriteDictionary(results);

            _connection.TrySendMessage(writer.CreateMessage());
        }

        private static void ReplyEmpty(MethodContext context)
        {
            using MessageWriter reply = context.CreateReplyWriter(null);

            context.Reply(reply.CreateMessage());
        }

        /// <summary>
        ///     The keys the desktop gave, a(sa{sv}): the push-to-talk key under the name the desktop shows.
        /// </summary>
        private static Dictionary<string, VariantValue> Shortcuts(bool isBound)
        {
            var shortcuts = new Tmds.DBus.Protocol.Array<Struct<string, Dict<string, VariantValue>>>();

            if (isBound)
            {
                shortcuts.Add(Struct.Create(DesktopPortal.SpeakShortcut, new Dict<string, VariantValue>
                {
                    ["description"] = "Echo: hold to speak",
                    ["trigger_description"] = "Ctrl+`",
                }));
            }

            return new Dictionary<string, VariantValue> { ["shortcuts"] = shortcuts };
        }
    }
}