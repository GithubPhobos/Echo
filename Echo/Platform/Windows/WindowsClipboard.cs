namespace Echo.Platform.Windows;

/// <summary>
///     The clipboard of Windows.
/// </summary>
/// <param name="clipboard">The clipboard library, behind its own interface so tests can replace it.</param>
[SupportedOSPlatform("windows")]
internal sealed class WindowsClipboard(IClipboard clipboard) : ITextClipboard
{
    private readonly IClipboard _clipboard = clipboard;

    /// <inheritdoc/>
    public Task SetTextAsync(string text) =>
        _clipboard.SetTextAsync(text);
}