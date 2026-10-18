using Echo.Listening;

namespace Echo.Platform.Linux;

/// <summary>
///     For sessions where the system does not tell which window is in front (Wayland keeps that from programs on
///     purpose): what the terminal itself reports about its window, while the working screen asks it to.
/// </summary>
/// <param name="reports">What the terminal said.</param>
internal sealed class ReportedTerminalFocus(TerminalFocusReports reports) : ITerminalFocus
{
    private readonly TerminalFocusReports _reports = reports;

    /// <inheritdoc/>
    public bool? IsFocused() => _reports.IsFocused;
}