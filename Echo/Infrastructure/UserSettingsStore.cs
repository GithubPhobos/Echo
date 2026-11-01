using System.Text.Json.Nodes;

namespace Echo.Infrastructure;

/// <inheritdoc cref="IUserSettingsStore"/>
/// <param name="paths"><see cref="IAppPaths"/></param>
internal sealed class UserSettingsStore(IAppPaths paths) : IUserSettingsStore
{
    private const string SetupSection = "Setup";
    private const char SectionSeparator = ':';

    /// <summary>
    ///     Enough digits for any setting, and none of the noise of a number stored in binary.
    /// </summary>
    private const string NumberFormat = "0.######";

    /// <summary>
    ///     Added to the name of a settings file that could not be read, when a fresh one takes its place.
    /// </summary>
    private const string DamagedSuffix = ".damaged";

    /// <summary>
    ///     Added to the name of the settings file for the file that only one may hold at a time.
    /// </summary>
    private const string LockSuffix = ".lock";

    /// <summary>
    ///     How long reading or changing the settings waits for another change, in this copy of Echo or another.
    /// </summary>
    private static readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonDocumentOptions _readOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,

        // Keeps non-Latin device names readable in the file
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IAppPaths _paths = paths;

    /// <inheritdoc/>
    public UserSettings Load()
    {
        JsonObject root = ReadAlone();

        // A value the setting cannot take, such as a word typed by hand where a number belongs, is left out:
        // the shipped value applies, and saving the settings replaces it
        AdditionalValues additional = AdditionalSettings.All.Aggregate(
            new AdditionalValues(),
            (values, setting) => values.With(
                setting,
                ReadText(root, setting.Section, setting.Key) is { } value && setting.Accepts(value) ? value : null));

        return new UserSettings
        {
            Additional = additional,
            SetupVersion = Read<int>(root, SetupSection, "Version"),
            UiLanguage = Read<string>(root, SetupSection, "UiLanguage"),
            // Settings saved before the name moved next to the microphone's number keep it in the setup section
            MicrophoneName = Read<string>(root, nameof(RecordingDeviceSettings), nameof(RecordingDeviceSettings.DeviceName))
                ?? Read<string>(root, SetupSection, "MicrophoneName"),
            DictationLanguage = Read<string>(root, nameof(WhisperSettings), nameof(WhisperSettings.Language)),
            HardwareBackend = Read<string>(root, nameof(WhisperSettings), nameof(WhisperSettings.HardwareBackend)),
            GraphicsCardIndex = Read<int?>(root, nameof(WhisperSettings), nameof(WhisperSettings.GpuDeviceIndex)),
            ModelName = Read<string>(root, nameof(WhisperSettings), nameof(WhisperSettings.ModelName)),
            HotkeyKey = Read<string>(root, nameof(PushToTalkSettings), nameof(PushToTalkSettings.Key)),
            HotkeyModifier = Read<string>(root, nameof(PushToTalkSettings), nameof(PushToTalkSettings.Modifier)),
            HotkeyLabel = Read<string>(root, nameof(PushToTalkSettings), nameof(PushToTalkSettings.DesktopShortcut)),
            MicrophoneIndex = Read<int?>(root, nameof(RecordingDeviceSettings), nameof(RecordingDeviceSettings.DeviceIndex)),
        };
    }

    /// <inheritdoc/>
    public void Save(UserSettings settings)
    {
        using FileStream alone = Lock();

        JsonObject root = ReadFile();

        Write(root, SetupSection, "Version", settings.SetupVersion);
        Write(root, SetupSection, "UiLanguage", settings.UiLanguage);
        Write<string>(root, SetupSection, "MicrophoneName", null);
        Write(root, nameof(WhisperSettings), nameof(WhisperSettings.Language), settings.DictationLanguage);
        Write(root, nameof(WhisperSettings), nameof(WhisperSettings.HardwareBackend), settings.HardwareBackend);
        Write(root, nameof(WhisperSettings), nameof(WhisperSettings.GpuDeviceIndex), settings.GraphicsCardIndex);
        Write(root, nameof(WhisperSettings), nameof(WhisperSettings.ModelName), settings.ModelName);
        Write(root, nameof(PushToTalkSettings), nameof(PushToTalkSettings.Key), settings.HotkeyKey);
        Write(root, nameof(PushToTalkSettings), nameof(PushToTalkSettings.Modifier), settings.HotkeyModifier);
        Write(root, nameof(PushToTalkSettings), nameof(PushToTalkSettings.DesktopShortcut), settings.HotkeyLabel);

        foreach (AdditionalSetting setting in AdditionalSettings.All)
            WriteText(root, setting, settings.Additional.Stored(setting));
        Write(root, nameof(RecordingDeviceSettings), nameof(RecordingDeviceSettings.DeviceIndex), settings.MicrophoneIndex);

        // The name goes next to the number, where the recording service finds the microphone by it
        Write(root, nameof(RecordingDeviceSettings), nameof(RecordingDeviceSettings.DeviceName), settings.MicrophoneName);

        WriteFile(root);
    }

    /// <inheritdoc/>
    public string? ReadValue(string section, string key) =>
        Read<string>(ReadAlone(), section, key);

    /// <inheritdoc/>
    public void WriteValue(string section, string key, string? value)
    {
        using FileStream alone = Lock();

        JsonObject root = ReadFile();

        Write(root, section, key, value);

        WriteFile(root);
    }

    /// <summary>
    ///     Writes the whole file. A file that could not be read is replaced by a fresh one, and what the user
    ///     wrote into it is kept aside.
    /// </summary>
    private void WriteFile(JsonObject root)
    {
        if (File.Exists(_paths.SettingsFile) && !IsReadable(_paths.SettingsFile))
            File.Copy(_paths.SettingsFile, _paths.SettingsFile + DamagedSuffix, overwrite: true);

        Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsFile)!);

        // Written next to the target first, so a crash never leaves a half-written settings file
        string temporaryFile = _paths.SettingsFile + ".tmp";
        File.WriteAllText(temporaryFile, root.ToJsonString(_writeOptions));
        File.Move(temporaryFile, _paths.SettingsFile, overwrite: true);
    }

    /// <summary>
    ///     Whether the settings file is there and holds settings that can be read. The configuration
    ///     takes the user's settings only from such a file, the same way <see cref="Load"/> does.
    /// </summary>
    public static bool IsReadable(string settingsFile) =>
        TryRead(settingsFile, out _);

    /// <summary>
    ///     The values in the settings file that their settings cannot take, each paired with the value the program
    ///     ships with (null when it ships without one). The configuration lays these over the file, so Echo runs
    ///     with what <see cref="Load"/> reads, not with a word typed by hand where a number belongs.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string?>> Rejected(string settingsFile)
    {
        if (!TryRead(settingsFile, out JsonObject root))
            return [];

        return AdditionalSettings.All
            .Where(setting => ReadText(root, setting.Section, setting.Key) is { } value && !setting.Accepts(value))
            .Select(setting => KeyValuePair.Create($"{setting.Section}{SectionSeparator}{setting.Key}", setting.Default))
            .ToList();
    }

    /// <summary>
    ///     The settings in the file. A file that cannot be read is treated as "not set up yet":
    ///     the wizard will write a fresh one.
    /// </summary>
    private JsonObject ReadFile() =>
        TryRead(_paths.SettingsFile, out JsonObject root) ? root : [];

    /// <summary>
    ///     The settings in the file, read while no change is under way.
    /// </summary>
    private JsonObject ReadAlone()
    {
        using FileStream alone = Lock();

        return ReadFile();
    }

    /// <summary>
    ///     Takes the settings for one reading or one change: a file next to them that only one may hold open at a
    ///     time, in this copy of Echo or another. Reading, changing and replacing the settings then happen as one
    ///     step: two changes at once both stay, and nobody reads the file while it is being replaced (a reading
    ///     that failed then would be taken for an empty file, and the next change would write only itself).
    /// </summary>
    /// <exception cref="IOException">Thrown if another change holds the settings for too long.</exception>
    private FileStream Lock()
    {
        string lockFile = _paths.SettingsFile + LockSuffix;
        Directory.CreateDirectory(Path.GetDirectoryName(lockFile)!);

        var waited = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (waited.Elapsed < _lockTimeout)
            {
                Thread.Sleep(10);
            }
        }
    }

    private static bool TryRead(string settingsFile, out JsonObject root)
    {
        root = [];

        try
        {
            if (File.Exists(settingsFile)
                &&
                JsonNode.Parse(File.ReadAllText(settingsFile), documentOptions: _readOptions) is JsonObject read)
            {
                root = read;

                return true;
            }
        }
        catch (Exception exc) when (exc is JsonException or IOException or UnauthorizedAccessException)
        {
            // Damaged or locked: reported as unreadable
        }

        return false;
    }

    private static T? Read<T>(JsonObject root, string section, string key)
    {
        try
        {
            return Section(root, section, create: false) is JsonObject values && values[key] is JsonNode node
                ? node.GetValue<T>()
                : default;
        }
        catch (Exception exc) when (exc is InvalidOperationException or FormatException)
        {
            return default;
        }
    }

    /// <summary>
    ///     Reads a value of any simple kind as text written the same way in every interface language.
    /// </summary>
    private static string? ReadText(JsonObject root, string section, string key)
    {
        if (Section(root, section, create: false) is not JsonObject values || values[key] is not JsonValue node)
            return null;

        return node.GetValueKind() switch
        {
            JsonValueKind.True => AdditionalSettings.On,
            JsonValueKind.False => AdditionalSettings.Off,
            JsonValueKind.Number => node.GetValue<double>().ToString(NumberFormat, CultureInfo.InvariantCulture),
            JsonValueKind.String => node.GetValue<string>(),
            _ => null
        };
    }

    /// <summary>
    ///     Writes a value in the kind the program expects it in; no value removes the key,
    ///     so the one that ships with the program applies.
    /// </summary>
    private static void WriteText(JsonObject root, AdditionalSetting setting, string? value)
    {
        switch (setting.Kind)
        {
            case SettingKind.Switch:
                Write(root, setting.Section, setting.Key, value is null ? (bool?)null : value == AdditionalSettings.On);

                break;

            case SettingKind.Whole:
                Write(root, setting.Section, setting.Key, value is null ? (int?)null : int.Parse(value, CultureInfo.InvariantCulture));

                break;

            case SettingKind.Fraction or SettingKind.Percent:
                Write(root, setting.Section, setting.Key, value is null ? (double?)null : double.Parse(value, CultureInfo.InvariantCulture));

                break;

            default:
                Write(root, setting.Section, setting.Key, value);

                break;
        }
    }

    private static void Write<T>(JsonObject root, string section, string key, T? value)
    {
        // There is nothing to remove from a section that is not there, and no reason to leave an empty one behind
        if (Section(root, section, create: value is not null) is not JsonObject values)
            return;

        if (value is null)
            values.Remove(key);
        else
            values[key] = JsonValue.Create(value);
    }

    /// <summary>
    ///     Finds a section by its path. The names of a section within a section are separated by a colon,
    ///     the way the configuration writes them.
    /// </summary>
    private static JsonObject? Section(JsonObject root, string path, bool create)
    {
        JsonObject section = root;

        foreach (string name in path.Split(SectionSeparator))
        {
            if (section[name] is not JsonObject inner)
            {
                if (!create)
                    return null;

                inner = [];
                section[name] = inner;
            }

            section = inner;
        }

        return section;
    }
}