namespace Echo.Platform.Windows;

/// <summary>
///     The Windows build of Whisper brings everything it needs.
/// </summary>
internal sealed class WindowsSystemLibraries : ISystemLibraries
{
    /// <inheritdoc/>
    public IReadOnlyList<string> FindMissing() => [];
}