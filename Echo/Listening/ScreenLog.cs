namespace Echo.Listening;

/// <summary>
///     The lines of the working screen's log, the newest last. Only the latest ones are kept:
///     the whole log is in the file. Safe to use from any thread.
/// </summary>
/// <param name="capacity">How many lines are kept.</param>
internal sealed class ScreenLog(int capacity = 5000)
{
    private readonly object _lock = new();
    private readonly List<ScreenEntry> _entries = [];
    private readonly int _capacity = capacity;

    /// <summary>
    ///     Grows by one with every line added, so a screen can tell whether it has anything new to show.
    /// </summary>
    public long Version { get; private set; }

    public void Add(ScreenEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);

            if (_entries.Count > _capacity)
                _entries.RemoveRange(0, _entries.Count - _capacity);

            Version++;
        }
    }

    public void Add(EntryKind kind, string text, string? note = null) =>
        Add(new ScreenEntry(DateTimeOffset.Now, kind, text, note));

    public IReadOnlyList<ScreenEntry> Snapshot()
    {
        lock (_lock)
            return [.. _entries];
    }
}