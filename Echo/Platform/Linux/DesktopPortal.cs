using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace Echo.Platform.Linux;

/// <summary>
///     The permission windows of the desktop (XDG portals), reached over D-Bus, the message bus of the session.
///     Wayland keeps the keyboard and the other windows from programs; a program asks the desktop instead, and
///     the desktop asks the user. Echo asks for three things: a key of its own (GlobalShortcuts), pressing keys
///     to paste (RemoteDesktop) and picking a file (FileChooser).
/// </summary>
/// <remarks>
///     One object serves the whole process: a session with the desktop lives as long as the connection that
///     made it, so the setup wizard, its test dictation and the working screen share the same connection and the
///     same sessions, and a permission given in the wizard holds on the working screen.
/// </remarks>
/// <param name="settings">Keeps the permission to paste between runs.</param>
/// <param name="address">The address of the session bus; tests give one of their own.</param>
/// <param name="launcher">
///     Echo's launcher in the applications menu, put in place before Echo names itself: the desktop does not
///     register a name it has no launcher for. Tests leave it out.
/// </param>
[SupportedOSPlatform("linux")]
internal sealed class DesktopPortal(IUserSettingsStore settings, string? address = null, DesktopEntry? launcher = null) : IDisposable
{
    /// <summary>
    ///     The name under which the desktop knows Echo. The permissions it gives are tied to it, so it never changes.
    /// </summary>
    public const string ApplicationId = "io.github.githubphobos.echo";

    /// <summary>
    ///     The name of the push-to-talk key among the keys Echo asks the desktop for.
    /// </summary>
    public const string SpeakShortcut = "speak";

    /// <summary>
    ///     RemoteDesktop: a session with the keyboard only, nothing of the screen.
    /// </summary>
    public const uint KeyboardDevice = 1;

    /// <summary>
    ///     RemoteDesktop: the user's permission is kept until the user takes it back.
    /// </summary>
    public const uint PersistUntilRevoked = 2;

    /// <summary>
    ///     Where the permission to paste is kept in the settings file.
    /// </summary>
    public const string SettingsSection = "Desktop";
    public const string TokenKey = "RemoteDesktopToken";

    private const string Service = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";
    private const string RequestInterface = "org.freedesktop.portal.Request";
    private const string SessionInterface = "org.freedesktop.portal.Session";
    private const string RegistryInterface = "org.freedesktop.host.portal.Registry";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";

    public const string ShortcutsInterface = "org.freedesktop.portal.GlobalShortcuts";
    public const string RemoteDesktopInterface = "org.freedesktop.portal.RemoteDesktop";
    public const string FileChooserInterface = "org.freedesktop.portal.FileChooser";

    /// <summary>
    ///     The answers of a permission window: granted, closed by the user, refused for another reason.
    /// </summary>
    private const uint ResponseSuccess = 0;

    /// <summary>
    ///     How often, and how far apart, Echo names itself again when the desktop has not yet noticed the launcher
    ///     Echo has just written.
    /// </summary>
    private const int RegisterAttempts = 5;
    private static readonly TimeSpan _registerPause = TimeSpan.FromMilliseconds(400);

    private readonly IUserSettingsStore _settings = settings;
    private readonly string? _address = address;
    private readonly DesktopEntry? _launcher = launcher;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<IDisposable> _watches = [];

    private DBusConnection? _connection;
    private int _requests;
    private string? _shortcutsSession;
    private string? _keyboardSession;

    /// <summary>
    ///     Why the desktop would not register Echo's name, when it would not: the reason it then refuses the key.
    /// </summary>
    private string? _registrationError;

    /// <summary>
    ///     A key Echo got from the desktop went down; the name of the key comes along.
    /// </summary>
    public event EventHandler<string>? ShortcutActivated;

    /// <summary>
    ///     A key Echo got from the desktop came up.
    /// </summary>
    public event EventHandler<string>? ShortcutDeactivated;

    /// <summary>
    ///     The user changed the push-to-talk key in the settings of the desktop; its new description comes along.
    /// </summary>
    public event EventHandler<string>? ShortcutChanged;

    /// <summary>
    ///     Whether the user gave Echo the permission to paste in an earlier run, so it can be renewed without asking.
    /// </summary>
    public bool HasKeyboardPermission =>
        !string.IsNullOrEmpty(_settings.ReadValue(SettingsSection, TokenKey));

    /// <summary>
    ///     The version of a portal, or null when the desktop does not offer it.
    /// </summary>
    public async Task<uint?> VersionAsync(string portalInterface)
    {
        try
        {
            DBusConnection connection = await ConnectAsync();

            return await connection.CallMethodAsync(
                CreateGetVersion(connection, portalInterface),
                (message, _) => message.GetBodyReader().ReadVariantValue().GetUInt32());
        }
        catch (Exception exc) when (IsUnavailable(exc))
        {
            return null;
        }
    }

    /// <summary>
    ///     Makes sure the desktop gives Echo its push-to-talk key, and returns how the desktop describes the key.
    ///     A key given in an earlier run is found again without asking; otherwise, or when
    ///     <paramref name="askAgain"/>, the desktop shows its window where the user confirms or changes the key.
    /// </summary>
    /// <param name="preferredTrigger">The key Echo suggests, in the notation of the XDG shortcuts standard, e.g. "CTRL+grave".</param>
    /// <param name="description">What the key does, as the desktop shows it to the user.</param>
    /// <param name="askAgain">Whether to show the window even when the desktop already gave a key.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    /// <returns>The description of the key, or null when the user closed the window without giving one.</returns>
    public async Task<string?> EnsureShortcutAsync(string preferredTrigger, string description, bool askAgain, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            DBusConnection connection = await ConnectAsync();

            _shortcutsSession ??= await CreateSessionAsync(connection, ShortcutsInterface, cancellationToken);

            if (!askAgain && FindTrigger(await ListShortcutsAsync(connection, cancellationToken)) is { } known)
                return known;

            (uint code, Dictionary<string, VariantValue> results) = await RequestAsync(
                connection,
                token => CreateBindShortcuts(connection, _shortcutsSession, token, preferredTrigger, description),
                cancellationToken);

            return code == ResponseSuccess && results.TryGetValue("shortcuts", out VariantValue shortcuts)
                ? FindTrigger(ToShortcuts(shortcuts)) ?? DesktopKeyNames.Readable(preferredTrigger)
                : null;
        }
        catch (DBusErrorReplyException exc) when (_registrationError is not null)
        {
            // "An app id is required" says little; the refusal to register says why
            throw new DBusErrorReplyException(exc.ErrorName, $"{exc.ErrorMessage} ({_registrationError})");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Makes sure Echo may press keys. With a permission kept from an earlier run the desktop renews it without
    ///     a window; otherwise the window is shown only when <paramref name="mayAsk"/>.
    /// </summary>
    /// <returns>Whether Echo may press keys now.</returns>
    public async Task<bool> EnsureKeyboardAsync(bool mayAsk, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (_keyboardSession is not null)
                return true;

            string? token = _settings.ReadValue(SettingsSection, TokenKey);

            if (string.IsNullOrEmpty(token) && !mayAsk)
                return false;

            DBusConnection connection = await ConnectAsync();
            string session = await CreateSessionAsync(connection, RemoteDesktopInterface, cancellationToken);

            (uint selected, _) = await RequestAsync(
                connection, requestToken => CreateSelectDevices(connection, session, requestToken, token), cancellationToken);

            if (selected != ResponseSuccess)
                return false;

            (uint started, Dictionary<string, VariantValue> results) = await RequestAsync(
                connection, requestToken => CreateStart(connection, session, requestToken), cancellationToken);

            // The kept permission is used up by every start; the desktop hands out a new one each time
            if (results.TryGetValue("restore_token", out VariantValue renewed))
                _settings.WriteValue(SettingsSection, TokenKey, renewed.GetString());

            bool hasKeyboard = started == ResponseSuccess
                && results.TryGetValue("devices", out VariantValue devices)
                && (devices.GetUInt32() & KeyboardDevice) != 0;

            if (!hasKeyboard)
            {
                // Refused, or granted without the keyboard: what was kept is worth nothing
                if (started != ResponseSuccess)
                    _settings.WriteValue(SettingsSection, TokenKey, null);

                return false;
            }

            _keyboardSession = session;

            return true;
        }
        catch (Exception exc) when (IsUnavailable(exc))
        {
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    ///     Presses the keys one after another and lets them go in reverse order, as a person presses a shortcut.
    /// </summary>
    /// <param name="keysyms">The keys, by their X11 keysyms.</param>
    /// <returns>Whether the keys were pressed; false when the permission is gone.</returns>
    public async Task<bool> PressAsync(params uint[] keysyms)
    {
        string? session = _keyboardSession;

        if (session is null || _connection is not { } connection)
            return false;

        try
        {
            foreach (uint keysym in keysyms)
                await connection.CallMethodAsync(CreateKeysym(connection, session, keysym, isPressed: true));

            foreach (uint keysym in keysyms.Reverse())
                await connection.CallMethodAsync(CreateKeysym(connection, session, keysym, isPressed: false));

            return true;
        }
        catch (Exception exc) when (IsUnavailable(exc))
        {
            _keyboardSession = null;

            return false;
        }
    }

    /// <summary>
    ///     Shows the file chooser of the desktop.
    /// </summary>
    /// <param name="title">The title of the window.</param>
    /// <param name="patterns">The file name patterns to show, such as "*.bin".</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    /// <returns>The full paths of the picked files; empty when the user closed the window.</returns>
    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<string> patterns, CancellationToken cancellationToken)
    {
        DBusConnection connection = await ConnectAsync();

        (uint code, Dictionary<string, VariantValue> results) = await RequestAsync(
            connection, token => CreateOpenFile(connection, token, title, patterns), cancellationToken);

        if (code != ResponseSuccess || !results.TryGetValue("uris", out VariantValue uris))
            return [];

        return
        [
            .. uris.GetArray<string>()
                .Select(uri => new Uri(uri))
                .Where(uri => uri.IsFile)
                .Select(uri => uri.LocalPath)
        ];
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable watch in _watches)
            watch.Dispose();

        _connection?.Dispose();
        _lock.Dispose();
    }

    /// <summary>
    ///     Connects once, and before anything else tells the desktop which application Echo is: newer portals
    ///     refuse programs that do not say so. Older portals have no such registry and need nothing.
    /// </summary>
    private async Task<DBusConnection> ConnectAsync()
    {
        if (_connection is not null)
            return _connection;

        var connection = new DBusConnection(_address ?? DBusAddress.Session ?? throw new InvalidOperationException("No session bus."));

        try
        {
            await connection.ConnectAsync();
            await RegisterAsync(connection);

            _watches.Add(await WatchAsync(connection, ObjectPath, ShortcutsInterface, "Activated", ReadShortcutSignal, value => OnShortcut(value, isPressed: true)));
            _watches.Add(await WatchAsync(connection, ObjectPath, ShortcutsInterface, "Deactivated", ReadShortcutSignal, value => OnShortcut(value, isPressed: false)));
            _watches.Add(await WatchAsync(connection, ObjectPath, ShortcutsInterface, "ShortcutsChanged", ReadShortcutsChanged, OnShortcutsChanged));

            _connection = connection;

            return connection;
        }
        catch
        {
            connection.Dispose();

            throw;
        }
    }

    /// <summary>
    ///     Names Echo to the desktop, its launcher in place first. A launcher written just now may take the desktop
    ///     a moment to notice, so the name is given again a few times before the refusal is taken as final.
    /// </summary>
    private async Task RegisterAsync(DBusConnection connection)
    {
        bool isNewLauncher = _launcher?.Ensure() == true;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await connection.CallMethodAsync(CreateRegister(connection));

                _registrationError = null;

                return;
            }
            catch (DBusErrorReplyException exc)
            {
                // A portal older than the registry knows Echo by other means, or not at all
                if (IsUnknownMethod(exc))
                    return;

                _registrationError = exc.ErrorMessage;

                if (!isNewLauncher || attempt == RegisterAttempts)
                    return;
            }

            await Task.Delay(_registerPause);
        }
    }

    private static bool IsUnknownMethod(DBusErrorReplyException exception) =>
        exception.ErrorName is "org.freedesktop.DBus.Error.UnknownMethod"
            or "org.freedesktop.DBus.Error.UnknownInterface"
            or "org.freedesktop.DBus.Error.UnknownObject"
            or "org.freedesktop.DBus.Error.ServiceUnknown";

    private async Task<string> CreateSessionAsync(DBusConnection connection, string portalInterface, CancellationToken cancellationToken)
    {
        (uint code, Dictionary<string, VariantValue> results) = await RequestAsync(
            connection, token => CreateCreateSession(connection, portalInterface, token), cancellationToken);

        if (code != ResponseSuccess || !results.TryGetValue("session_handle", out VariantValue handle))
            throw new DBusErrorReplyException("org.freedesktop.portal.Error.Failed", "The desktop did not open a session.");

        string session = handle.Type == VariantValueType.ObjectPath ? handle.GetObjectPathAsString() : handle.GetString();

        // A session the desktop closes, because the user took the permission back, is forgotten
        _watches.Add(await WatchAsync(connection, session, SessionInterface, "Closed", (_, _) => true, _ => OnSessionClosed(session)));

        return session;
    }

    private async Task<IReadOnlyList<(string Id, Dictionary<string, VariantValue> Properties)>> ListShortcutsAsync(DBusConnection connection,
                                                                                                                CancellationToken cancellationToken)
    {
        (uint code, Dictionary<string, VariantValue> results) = await RequestAsync(
            connection, token => CreateListShortcuts(connection, _shortcutsSession!, token), cancellationToken);

        return code == ResponseSuccess && results.TryGetValue("shortcuts", out VariantValue shortcuts) ? ToShortcuts(shortcuts) : [];
    }

    /// <summary>
    ///     A call that the desktop answers later, once the user has dealt with its window: the answer comes as
    ///     a signal on an object whose name follows from the token sent with the call. The signal is waited for
    ///     before the call is made, so an answer that comes at once is not missed.
    /// </summary>
    private async Task<(uint Code, Dictionary<string, VariantValue> Results)> RequestAsync(DBusConnection connection,
                                                                                            Func<string, MessageBuffer> createCall,
                                                                                            CancellationToken cancellationToken)
    {
        string token = $"{ApplicationInfo.CommandName}{Interlocked.Increment(ref _requests)}";
        string requestPath = RequestPath(connection.UniqueName, token);

        var response = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>(TaskCreationOptions.RunContinuationsAsynchronously);

        using IDisposable watch = await WatchAsync(
            connection,
            requestPath,
            RequestInterface,
            "Response",
            (message, _) =>
            {
                Reader reader = message.GetBodyReader();

                return (reader.ReadUInt32(), reader.ReadDictionaryOfStringToVariantValue());
            },
            value => response.TrySetResult(value),
            exception => response.TrySetException(exception ?? new DBusConnectionClosedException("The connection to the desktop was closed.")));

        await connection.CallMethodAsync(createCall(token), (message, _) => message.GetBodyReader().ReadObjectPathAsString());

        return await response.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    ///     Where the desktop puts the answer to a request: the unique name of the connection, without its colon
    ///     and with dots made underscores, and the token.
    /// </summary>
    internal static string RequestPath(string? uniqueName, string token) =>
        $"{ObjectPath}/request/{(uniqueName ?? string.Empty).TrimStart(':').Replace('.', '_')}/{token}";

    /// <summary>
    ///     Converts a key of the settings into the notation of the XDG shortcuts standard, e.g. "CTRL+grave".
    /// </summary>
    internal static string? ToTrigger(Setup.Hotkeys.HotkeyChoice hotkey)
    {
        if (KeyMapperService.ParseMainKey(hotkey.Key) is not { } key || XKeys.Find(key) is not { } found)
            return null;

        return XKeys.FindModifier(hotkey.Modifier) is { } modifier ? $"{modifier.Name}+{found.Name}" : found.Name;
    }

    /// <summary>
    ///     How the desktop describes the push-to-talk key, from a list of shortcuts; null when the list has
    ///     no key for it.
    /// </summary>
    internal static string? FindTrigger(IReadOnlyList<(string Id, Dictionary<string, VariantValue> Properties)> shortcuts)
    {
        foreach ((string id, Dictionary<string, VariantValue> properties) in shortcuts)
        {
            if (id == SpeakShortcut)
            {
                // The desktop's own words around the Linux name of the key; Echo shows the key its own way
                return properties.TryGetValue("trigger_description", out VariantValue trigger) && trigger.GetString() is { Length: > 0 } text
                    ? DesktopKeyNames.Readable(text)
                    : null;
            }
        }

        return null;
    }

    /// <summary>
    ///     A list of shortcuts, a(sa{sv}), as it comes inside the results of a request.
    /// </summary>
    private static IReadOnlyList<(string Id, Dictionary<string, VariantValue> Properties)> ToShortcuts(VariantValue shortcuts) =>
    [
        .. Enumerable.Range(0, shortcuts.Count)
            .Select(shortcuts.GetItem)
            .Select(shortcut => (shortcut.GetItem(0).GetString(), shortcut.GetItem(1).GetDictionary<string, VariantValue>()))
    ];

    private static (string Session, string Shortcut) ReadShortcutSignal(Message message, object? state)
    {
        Reader reader = message.GetBodyReader();

        return (reader.ReadObjectPathAsString(), reader.ReadString());
    }

    /// <summary>
    ///     A list of shortcuts, a(sa{sv}), as it comes in a signal.
    /// </summary>
    private static (string Session, IReadOnlyList<(string Id, Dictionary<string, VariantValue> Properties)> Shortcuts) ReadShortcutsChanged(Message message,
                                                                                                                                           object? state)
    {
        Reader reader = message.GetBodyReader();

        string session = reader.ReadObjectPathAsString();
        var shortcuts = new List<(string, Dictionary<string, VariantValue>)>();

        ArrayEnd end = reader.ReadArrayStart(DBusType.Struct);
        while (reader.HasNext(end))
        {
            reader.AlignStruct();
            shortcuts.Add((reader.ReadString(), reader.ReadDictionaryOfStringToVariantValue()));
        }

        return (session, shortcuts);
    }

    /// <summary>
    ///     Listens to a signal; <paramref name="onEnd"/> hears when listening ends because the connection is gone.
    /// </summary>
    private static async Task<IDisposable> WatchAsync<T>(DBusConnection connection,
                                                         string path,
                                                         string signalInterface,
                                                         string signal,
                                                         MessageValueReader<T> reader,
                                                         Action<T> onValue,
                                                         Action<Exception?>? onEnd = null) =>
        await connection.WatchSignalAsync(
            null,
            path,
            signalInterface,
            signal,
            reader,
            notification =>
            {
                if (notification.HasValue)
                    onValue(notification.Value);
                else if (notification.IsCompletion)
                    onEnd?.Invoke(notification.Exception);
            },
            ObserverFlags.None,
            false,
            null);

    private void OnShortcut((string Session, string Shortcut) value, bool isPressed)
    {
        if (value.Session != _shortcutsSession)
            return;

        (isPressed ? ShortcutActivated : ShortcutDeactivated)?.Invoke(this, value.Shortcut);
    }

    private void OnShortcutsChanged((string Session, IReadOnlyList<(string Id, Dictionary<string, VariantValue> Properties)> Shortcuts) value)
    {
        if (value.Session == _shortcutsSession && FindTrigger(value.Shortcuts) is { } trigger)
            ShortcutChanged?.Invoke(this, trigger);
    }

    private void OnSessionClosed(string session)
    {
        if (_keyboardSession == session)
            _keyboardSession = null;

        if (_shortcutsSession == session)
            _shortcutsSession = null;
    }

    /// <summary>
    ///     Errors that mean "no such portal here" rather than a fault of Echo.
    /// </summary>
    private static bool IsUnavailable(Exception exception) =>
        exception is DBusExceptionBase or InvalidOperationException;

    private static MessageBuffer CreateRegister(DBusConnection connection)
    {
        using MessageWriter writer = connection.GetMessageWriter();

        writer.WriteMethodCallHeader(Service, ObjectPath, RegistryInterface, "Register", "sa{sv}", MessageFlags.None);
        writer.WriteString(ApplicationId);
        writer.WriteDictionary(new Dictionary<string, VariantValue>());

        return writer.CreateMessage();
    }

    private static MessageBuffer CreateGetVersion(DBusConnection connection, string portalInterface)
    {
        using MessageWriter writer = connection.GetMessageWriter();

        writer.WriteMethodCallHeader(Service, ObjectPath, PropertiesInterface, "Get", "ss", MessageFlags.None);
        writer.WriteString(portalInterface);
        writer.WriteString("version");

        return writer.CreateMessage();
    }

    private static MessageBuffer CreateCreateSession(DBusConnection connection, string portalInterface, string token)
    {
        using MessageWriter writer = connection.GetMessageWriter();

        writer.WriteMethodCallHeader(Service, ObjectPath, portalInterface, "CreateSession", "a{sv}", MessageFlags.None);
        writer.WriteDictionary(new Dictionary<string, VariantValue>
        {
            ["handle_token"] = token,
            ["session_handle_token"] = token,
        });

        return writer.CreateMessage();
    }

    private static MessageBuffer CreateBindShortcuts(DBusConnection connection, string session, string token, string preferredTrigger, string description)
    {
        using MessageWriter writer = connection.GetMessageWriter();

        writer.WriteMethodCallHeader(Service, ObjectPath, ShortcutsInterface, "BindShortcuts", "oa(sa{sv})sa{sv}", MessageFlags.None);
        writer.WriteObjectPath(session);

        ArrayStart shortcuts = writer.WriteArrayStart(DBusType.Struct);
        writer.WriteStructureStart();
        writer.WriteString(SpeakShortcut);
        writer.WriteDictionary(new Dictionary<string, VariantValue>
        {
            ["description"] = description,
            ["preferred_trigger"] = preferredTrigger,
        });
        writer.WriteArrayEnd(shortcuts);

        // No window of Echo's own to put the desktop's window over: a terminal is not one the portal knows
        writer.WriteString(string.Empty);
        writer.WriteDictionary(new Dictionary<string, VariantValue> { ["handle_token"] = token });

        return writer.CreateMessage();
    }

    private static MessageBuffer CreateListShortcuts(DBusConnection connection, string session, string token)
    {
        using MessageWriter writer = connection.GetMessageWriter();

        writer.WriteMethodCallHeader(Service, ObjectPath, ShortcutsInterface, "ListShortcuts", "oa{sv}", MessageFlags.None);
        writer.WriteObjectPath(session);
        writer.WriteDictionary(new Dictionary<string, VariantValue> { ["handle_token"] = token });

        return writer.CreateMessage();
    }

    private static MessageBuffer CreateSelectDevices(DBusConnection connection, string session, string token, string? restoreToken)
    {
        using MessageWriter writer = connection.GetMessageWriter();

        var options = new Dictionary<string, VariantValue>
        {
            ["handle_token"] = token,
            ["types"] = KeyboardDevice,
            ["persist_mode"] = PersistUntilRevoked,
        };

        if (!string.IsNullOrEmpty(restoreToken))
            options["restore_token"] = restoreToken;

        writer.WriteMethodCallHeader(Service, ObjectPath, RemoteDesktopInterface, "SelectDevices", "oa{sv}", MessageFlags.None);
        writer.WriteObjectPath(session);
        writer.WriteDictionary(options);

        return writer.CreateMessage();
    }

    private static MessageBuffer CreateStart(DBusConnection connection, string session, string token)
    {
        using MessageWriter writer = connection.GetMessageWriter();

        writer.WriteMethodCallHeader(Service, ObjectPath, RemoteDesktopInterface, "Start", "osa{sv}", MessageFlags.None);
        writer.WriteObjectPath(session);
        writer.WriteString(string.Empty);
        writer.WriteDictionary(new Dictionary<string, VariantValue> { ["handle_token"] = token });

        return writer.CreateMessage();
    }

    private static MessageBuffer CreateKeysym(DBusConnection connection, string session, uint keysym, bool isPressed)
    {
        using MessageWriter writer = connection.GetMessageWriter();

        writer.WriteMethodCallHeader(Service, ObjectPath, RemoteDesktopInterface, "NotifyKeyboardKeysym", "oa{sv}iu", MessageFlags.None);
        writer.WriteObjectPath(session);
        writer.WriteDictionary(new Dictionary<string, VariantValue>());
        writer.WriteInt32((int)keysym);
        writer.WriteUInt32(isPressed ? 1u : 0u);

        return writer.CreateMessage();
    }

    /// <summary>
    ///     OpenFile with the patterns as one filter. A filter is a(sa(us)): a name and its patterns, each with
    ///     its kind (0 for a glob pattern); it is written by hand, as a variant of that signature.
    /// </summary>
    private static MessageBuffer CreateOpenFile(DBusConnection connection, string token, string title, IReadOnlyList<string> patterns)
    {
        using MessageWriter writer = connection.GetMessageWriter();

        writer.WriteMethodCallHeader(Service, ObjectPath, FileChooserInterface, "OpenFile", "ssa{sv}", MessageFlags.None);
        writer.WriteString(string.Empty);
        writer.WriteString(title);

        ArrayStart options = writer.WriteDictionaryStart();

        writer.WriteDictionaryEntryStart();
        writer.WriteString("handle_token");
        writer.WriteVariantString(token);

        writer.WriteDictionaryEntryStart();
        writer.WriteString("multiple");
        writer.WriteVariantBool(true);

        if (patterns.Count > 0)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString("filters");
            writer.WriteSignature("a(sa(us))");

            ArrayStart filters = writer.WriteArrayStart(DBusType.Struct);
            writer.WriteStructureStart();
            writer.WriteString(string.Join(", ", patterns));

            ArrayStart globs = writer.WriteArrayStart(DBusType.Struct);
            foreach (string pattern in patterns)
            {
                writer.WriteStructureStart();
                writer.WriteUInt32(0);
                writer.WriteString(pattern);
            }
            writer.WriteArrayEnd(globs);

            writer.WriteArrayEnd(filters);
        }

        writer.WriteDictionaryEnd(options);

        return writer.CreateMessage();
    }
}