using Microsoft.Win32;

namespace Echo.Platform.Windows;

/// <inheritdoc cref="ISessionEvents"/>
/// <remarks>
///     Windows tells a program about the sessions through a window. Echo has none, so .NET opens a hidden one
///     on a thread of its own, as long as the first subscriber is not a thread of the single-threaded kind.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class WindowsSessionEvents : ISessionEvents, IDisposable
{
    /// <inheritdoc/>
    public event EventHandler? Returned;

    public WindowsSessionEvents() =>
        SystemEvents.SessionSwitch += OnSessionSwitch;

    /// <inheritdoc/>
    public void Dispose() =>
        SystemEvents.SessionSwitch -= OnSessionSwitch;

    /// <summary>
    ///     Switching to another account and connecting remotely both disconnect the session, and taking it back
    ///     connects it again. Locking the screen keeps the session and its sound, so unlocking is not a return.
    /// </summary>
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            Returned?.Invoke(this, EventArgs.Empty);
    }
}