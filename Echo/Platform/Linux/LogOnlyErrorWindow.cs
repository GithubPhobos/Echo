namespace Echo.Platform.Linux;

/// <summary>
///     Linux has no message window every program can count on, so the error stays in the log, where it already is.
/// </summary>
internal sealed class LogOnlyErrorWindow : IErrorWindow
{
    /// <inheritdoc/>
    public void Show(string message)
    {
    }
}