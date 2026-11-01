namespace Echo.Listening;

/// <summary>
///     The focus of this terminal session, reported by its input stream rather than its outer window.
/// </summary>
internal sealed class TerminalFocusReports
{
    public const string Enable = "\u001b[?1004h";
    public const string Disable = "\u001b[?1004l";
    private readonly object _lock = new();
    private bool? _isFocused;

    /// <summary>Null until a real focus event arrives; no immediate reply is assumed.</summary>
    public bool? IsFocused { get { lock (_lock) return _isFocused; } }

    /// <summary>Starts a fresh screen without the previous screen's state.</summary>
    public void Started() { lock (_lock) _isFocused = null; }

    /// <summary>Records an actual event, however long after startup it arrives.</summary>
    public void Report(bool focused) { lock (_lock) _isFocused = focused; }

    /// <summary>Forgets the reports when the screen is left.</summary>
    public void Stopped() => Started();
}