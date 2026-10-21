using System.Diagnostics;
using System.Runtime.Versioning;
using Echo.Platform.Linux;
using Tmds.DBus.Protocol;

namespace Echo.Tests.UnitTests;

/// <summary>
///     A desktop that answers like the XDG portals do, on a message bus started for the test alone. It keeps the rules
///     of the real portals that Echo has to live with: a session takes keys once, a session can be closed by the
///     desktop, and the portal service can be restarted while Echo runs.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class FakeDesktop : IPathMethodHandler, IAsyncDisposable
{
    /// <summary>
    ///     The message bus program the stand-in runs on.
    /// </summary>
    public const string BusProgram = "/usr/bin/dbus-daemon";

    private const string Service = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";

    private readonly Process _bus;
    private readonly object _lock = new();
    private readonly HashSet<string> _boundSessions = [];

    private DBusConnection _connection;
    private string? _client;
    private Action? _answerBind;

    private FakeDesktop(Process bus, string address, DBusConnection connection)
    {
        _bus = bus;
        _connection = connection;
        Address = address;
    }

    public string Address { get; }

    public string Path => ObjectPath;

    /// <summary>
    ///     Requests and sessions are objects below the desktop's own: Echo closes them there.
    /// </summary>
    public bool HandlesChildPaths => true;

    public List<string> Calls { get; } = [];

    public string? RegisteredId { get; private set; }

    /// <summary>
    ///     How many times Echo has made a call, such as "org.freedesktop.portal.Request.Close"; safe to ask while the
    ///     desktop is still answering.
    /// </summary>
    public int CountOf(string call)
    {
        lock (_lock)
            return Calls.Count(made => made == call);
    }

    /// <summary>
    ///     When set, the desktop registers only a name it has a launcher for in this folder, and opens no
    ///     session for a program it could not register, the way GNOME 50 on Ubuntu 26.04 does.
    /// </summary>
    public string? LaunchersDirectory { get; set; }

    public string? PreferredTrigger { get; private set; }

    /// <summary>
    ///     What the key does, as Echo asked the desktop to show it.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    ///     Whether the desktop gives Echo its key. Kept by the desktop, also when its portal service is restarted.
    /// </summary>
    public bool IsBound { get; set; }

    /// <summary>
    ///     When set, the window that gives the key stays open until <see cref="AnswerBind"/>, as if the user had not
    ///     dealt with it yet.
    /// </summary>
    public bool HoldsBind { get; set; }

    /// <summary>
    ///     Done once the window that gives the key is open.
    /// </summary>
    public TaskCompletionSource BindShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string IssuedToken { get; set; } = "token";

    public string? ReceivedToken { get; private set; }

    public (uint Types, uint Persist) SelectedDevices { get; private set; }

    /// <summary>
    ///     The sessions of the keys and of the keyboard the desktop opened last.
    /// </summary>
    public string? ShortcutsSession { get; private set; }

    public string? KeyboardSession { get; private set; }

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

        var desktop = new FakeDesktop(bus, address, await ConnectAsync(address));
        desktop._connection.AddMethodHandler(desktop);

        return desktop;
    }

    /// <summary>
    ///     The user presses or lets go of a key the desktop gave.
    /// </summary>
    public void Press(string shortcut, bool isDown)
    {
        using MessageWriter writer = _connection.GetMessageWriter();

        writer.WriteSignalHeader(_client, ObjectPath, DesktopPortal.ShortcutsInterface, isDown ? "Activated" : "Deactivated", "osta{sv}");
        writer.WriteObjectPath(ShortcutsSession!);
        writer.WriteString(shortcut);
        writer.WriteUInt64(1);
        writer.WriteDictionary(new Dictionary<string, VariantValue>());

        _connection.TrySendMessage(writer.CreateMessage());
    }

    /// <summary>
    ///     The user deals with the window that gives the key.
    /// </summary>
    public void AnswerBind() => _answerBind!();

    /// <summary>
    ///     The desktop closes a session, as when the user takes a permission back.
    /// </summary>
    public void CloseSession(string session)
    {
        using MessageWriter writer = _connection.GetMessageWriter();

        writer.WriteSignalHeader(_client, session, "org.freedesktop.portal.Session", "Closed", "a{sv}");
        writer.WriteDictionary(new Dictionary<string, VariantValue>());

        _connection.TrySendMessage(writer.CreateMessage());
    }

    /// <summary>
    ///     The portal service ends and a new one takes its name. The new one knows no program and no session; what
    ///     the user gave Echo stays with the desktop.
    /// </summary>
    public async Task RestartAsync()
    {
        _connection.Dispose();

        lock (_lock)
        {
            RegisteredId = null;
            ShortcutsSession = null;
            KeyboardSession = null;
            _boundSessions.Clear();
        }

        _connection = await ConnectAsync(Address);
        _connection.AddMethodHandler(this);
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
                    ShortcutsSession = session;
                else
                    KeyboardSession = session;

                Respond(context, request, options, new Dictionary<string, VariantValue> { ["session_handle"] = session });

                break;

            case "ListShortcuts":
                reader.ReadObjectPath();
                Respond(context, request, reader.ReadDictionaryOfStringToVariantValue(), Shortcuts(IsBound));

                break;

            case "BindShortcuts":
                string boundSession = reader.ReadObjectPathAsString();

                // The rule of the real portal: a session takes its keys once
                lock (_lock)
                {
                    if (!_boundSessions.Add(boundSession))
                    {
                        context.ReplyError("org.freedesktop.portal.Error.NotAllowed", "BindShortcuts may only be called once per session.");

                        break;
                    }
                }

                ArrayEnd end = reader.ReadArrayStart(DBusType.Struct);
                while (reader.HasNext(end))
                {
                    reader.AlignStruct();
                    reader.ReadString();

                    Dictionary<string, VariantValue> shortcut = reader.ReadDictionaryOfStringToVariantValue();
                    PreferredTrigger = shortcut["preferred_trigger"].GetString();
                    Description = shortcut["description"].GetString();
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

            case "Close":
                // A request or a session closed by Echo
                ReplyEmpty(context);

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

    private static async Task<DBusConnection> ConnectAsync(string address)
    {
        var connection = new DBusConnection(address);

        await connection.ConnectAsync();
        await connection.RequestNameAsync(Service, RequestNameOptions.Default);

        return connection;
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

        if (HoldsBind && request.MemberAsString == "BindShortcuts")
        {
            _answerBind = () => SendResponse(sender, requestPath, results);
            BindShown.TrySetResult();

            return;
        }

        SendResponse(sender, requestPath, results);
    }

    private void SendResponse(string? sender, string requestPath, Dictionary<string, VariantValue> results)
    {
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