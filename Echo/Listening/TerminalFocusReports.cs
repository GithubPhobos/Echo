namespace Echo.Listening;

/// <summary>
///     What the terminal itself says about its window: it becomes active, it stops being active. Echo asks for these
///     reports while the working screen is up, so that where the system does not say which window is in front
///     (Wayland), a phrase said while the user looks at Echo still goes to the log, not into the command line.
/// </summary>
/// <remarks>
///     .NET reads the reports as keys: "ESC [ I" (active) as Page Up and "ESC [ O" (not active) as F3. Page Up is also
///     a real key, so the reports are believed only from a terminal that has shown it sends them: the terminal of
///     GNOME (VTE) answers right away with the state of its window. A terminal that does not leaves Echo as it was:
///     the phrase is typed into the window in front.
/// </remarks>
/// <param name="time">The clock, for the moment right after the reports are asked for.</param>
internal sealed class TerminalFocusReports(TimeProvider time)
{
    /// <summary>
    ///     Asks the terminal for the reports, and tells it to stop (DECSET and DECRST 1004).
    /// </summary>
    public const string Enable = "\u001b[?1004h";
    public const string Disable = "\u001b[?1004l";

    /// <summary>
    ///     How soon after the reports are asked for the terminal's first answer is expected.
    /// </summary>
    private static readonly TimeSpan _firstAnswer = TimeSpan.FromMilliseconds(500);

    private readonly TimeProvider _time = time;
    private readonly object _lock = new();

    private long _askedAt;
    private bool _isAnswered;
    private bool? _isFocused;

    /// <summary>
    ///     Whether the terminal's window is active, as the terminal said; null when it has said nothing.
    /// </summary>
    public bool? IsFocused
    {
        get
        {
            lock (_lock)
                return _isFocused;
        }
    }

    /// <summary>
    ///     Called when the reports have just been asked for: what came before is forgotten.
    /// </summary>
    public void Started()
    {
        lock (_lock)
        {
            _askedAt = _time.GetTimestamp();
            _isAnswered = false;
            _isFocused = null;
        }
    }

    /// <summary>
    ///     Called when the reports are no longer asked for: the terminal says nothing any more.
    /// </summary>
    public void Stopped()
    {
        lock (_lock)
        {
            _isAnswered = false;
            _isFocused = null;
        }
    }

    /// <summary>
    ///     Takes a key that is a report of the terminal. Returns false for a key the user pressed.
    /// </summary>
    public bool Handle(ConsoleKeyInfo key)
    {
        if (key.Modifiers != 0 || key.Key is not (ConsoleKey.PageUp or ConsoleKey.F3))
            return false;

        bool isFocusIn = key.Key == ConsoleKey.PageUp;

        lock (_lock)
        {
            // The first answer comes at once or never: a terminal that has not answered by then sends no reports
            if (!_isAnswered)
            {
                if (_time.GetElapsedTime(_askedAt) > _firstAnswer)
                    return false;

                _isAnswered = true;
                _isFocused = isFocusIn;

                return true;
            }

            // Not active: F3 is the report, and the next Page Up is the report of the window becoming active again.
            // Active: Page Up is the user's own, scrolling the log
            if (!isFocusIn)
            {
                _isFocused = false;

                return true;
            }

            if (_isFocused == false)
            {
                _isFocused = true;

                return true;
            }

            return false;
        }
    }
}