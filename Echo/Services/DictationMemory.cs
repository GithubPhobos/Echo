namespace Echo.Services;

/// <inheritdoc cref="IDictationMemory"/>
/// <remarks>
///     A phrase is one JSON line appended to "&lt;year&gt;-&lt;month&gt;.jsonl" in the folder of the history. The files
///     are only ever added to; only their last byte is read to preserve line boundaries after an interrupted write.
///     However large they grow, Echo's memory does not.
/// </remarks>
/// <param name="paths">Where the history lives.</param>
/// <param name="memorySettings">Whether the user keeps the history.</param>
/// <param name="whisperSettings">The model and the language the phrases are recognized with.</param>
/// <param name="recordingSettings">The silence level the phrases are recorded with.</param>
/// <param name="time">The time of a phrase, and the month its file is named after.</param>
/// <param name="runtime">The library actually loaded for recognition.</param>
/// <param name="defaults">The bundled defaults to compare user settings with.</param>
internal sealed class DictationMemory(IAppPaths paths,
                                      IOptions<MemorySettings> memorySettings,
                                      IOptions<WhisperSettings> whisperSettings,
                                      IOptions<RecordingDeviceSettings> recordingSettings,
                                      TimeProvider time,
                                      IRecognitionRuntime? runtime = null,
                                      WhisperSettings? defaults = null) : IDictationMemory
{
    /// <summary>
    ///     Letters of every language stay as they are, readable in the file; quotes and line breaks are escaped,
    ///     so a phrase is always one line.
    /// </summary>
    private static readonly JsonSerializerOptions _json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    ///     A history held open elsewhere must not keep dictation waiting indefinitely.
    /// </summary>
    private static readonly TimeSpan _writeTimeout = TimeSpan.FromMilliseconds(250);

    private readonly IAppPaths _paths = paths;
    private readonly MemorySettings _memorySettings = memorySettings.Value;
    private readonly WhisperSettings _whisperSettings = whisperSettings.Value;
    private readonly RecordingDeviceSettings _recordingSettings = recordingSettings.Value;
    private readonly TimeProvider _time = time;
    private readonly IRecognitionRuntime _runtime = runtime ?? new RecognitionRuntime();
    private readonly IReadOnlyDictionary<string, object?> _custom = DictationMemoryFormat.CustomSettings(whisperSettings.Value, defaults ?? DictationMemoryFormat.Defaults());

    /// <summary>
    ///     Two phrases are never written into a file at the same time.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     Lines for the log file only.
    /// </summary>
    private static Serilog.ILogger FileLog => Log.ForContext(ScreenLogSink.HiddenProperty, true);

    /// <inheritdoc/>
    public void Add(string text, TimeSpan held, TimeSpan audio, TimeSpan recognition)
    {
        if (!_memorySettings.Enabled)
            return;

        try
        {
            DateTimeOffset now = _time.GetLocalNow();

            var entry = new Dictionary<string, object?>
            {
                ["ts"] = now.DateTime,
                ["rt"] = text,
                ["held"] = Seconds(held),
                ["dur"] = Seconds(audio),
                ["rd"] = Seconds(recognition),
                ["mdl"] = DictationMemoryFormat.ModelCode(_whisperSettings.ModelName),
                ["v"] = ApplicationInfo.Version,
                ["lng"] = _whisperSettings.Language,
                ["et"] = _whisperSettings.Translate,
                ["st"] = _recordingSettings.SilenceThreshold,
                ["acc"] = _runtime.Loaded switch
                {
                    null => null,
                    RuntimeLibrary.Cuda => "cuda",
                    RuntimeLibrary.Vulkan => "vulkan",
                    _ => "cpu",
                },
            };
            if (_custom.Count > 0)
                entry["ws"] = _custom;

            string line = JsonSerializer.Serialize(entry, _json);

            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
            string file = Path.Combine(_paths.MemoryDirectory, now.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".jsonl");

            Directory.CreateDirectory(_paths.MemoryDirectory);

            lock (_lock)
            {
                // Checking the tail and appending share the same exclusive interprocess lock.
                using FileStream stream = OpenForAppend(file);
                if (stream.Length > 0)
                {
                    stream.Seek(-1, SeekOrigin.End);
                    int last = stream.ReadByte();
                    stream.Seek(0, SeekOrigin.End);
                    if (last != '\n')
                        stream.WriteByte((byte)'\n');
                }
                stream.Write(bytes);
            }
        }
        catch (Exception exc) when (exc is not OutOfMemoryException)
        {
            FileLog.Warning("Could not keep the phrase in the dictation history: {Reason}", exc.Message);
        }
    }

    /// <summary>
    ///     Opens the month's file for one append, including when another instance is writing to it.
    ///     The wait is bounded; a file that stays busy is handled as any other history I/O failure.
    /// </summary>
    private static FileStream OpenForAppend(string file)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, bufferSize: 1);
            }
            catch (IOException) when (waited.Elapsed < _writeTimeout)
            {
                Thread.Sleep(10);
            }
        }
    }

    /// <summary>
    ///     Durations use hundredths of a second in the history.
    /// </summary>
    private static double Seconds(TimeSpan span) =>
        Math.Round(span.TotalSeconds, 2);
}