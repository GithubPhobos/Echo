namespace Echo.Listening;

/// <summary>
///     Writes the log into one file a day in the logs folder and keeps only the latest days.
/// </summary>
internal sealed class DailyFileSink : ILogEventSink, IDisposable
{
    public const int KeptDays = 7;

    private const string Prefix = "echo-";
    private const string Extension = ".log";
    private const string DateFormat = "yyyyMMdd";

    private readonly object _lock = new();
    private readonly string _directory;

    private StreamWriter? _writer;
    private string? _day;

    /// <param name="directory">The logs folder; it is created when it does not exist.</param>
    public DailyFileSink(string directory)
    {
        _directory = directory;

        try
        {
            Directory.CreateDirectory(directory);
            RemoveOldFiles();
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            // A log that cannot be written must not stop dictation
        }
    }

    /// <summary>
    ///     The file the log of the given day goes to.
    /// </summary>
    public static string FileName(DateTimeOffset day) =>
        Prefix + day.ToString(DateFormat, CultureInfo.InvariantCulture) + Extension;

    /// <inheritdoc/>
    public void Emit(LogEvent logEvent)
    {
        string line =
            $"{logEvent.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level(logEvent.Level)}] {LogText.Message(logEvent)}";

        lock (_lock)
        {
            try
            {
                StreamWriter writer = WriterFor(logEvent.Timestamp);

                writer.WriteLine(line);

                if (logEvent.Exception is not null)
                    writer.WriteLine(logEvent.Exception);

                writer.Flush();
            }
            catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
            {
                _writer = null;
                _day = null;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private StreamWriter WriterFor(DateTimeOffset time)
    {
        string day = time.ToString(DateFormat, CultureInfo.InvariantCulture);

        if (_writer is not null && _day == day)
            return _writer;

        _writer?.Dispose();

        // Shared, so the file can be read while Echo runs
        var stream = new FileStream(Path.Combine(_directory, FileName(time)), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _day = day;

        RemoveOldFiles();

        return _writer;
    }

    /// <summary>
    ///     Keeps the files of the latest <see cref="KeptDays"/> days. Their names sort by date.
    /// </summary>
    private void RemoveOldFiles()
    {
        string[] files = Directory.GetFiles(_directory, Prefix + "*" + Extension);

        Array.Sort(files, StringComparer.Ordinal);

        foreach (string file in files.Take(Math.Max(0, files.Length - KeptDays)))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
            {
                // Still open somewhere: it goes next time
            }
        }
    }

    private static string Level(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "VRB",
        LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        _ => "FTL"
    };
}