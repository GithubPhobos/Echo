namespace Echo.Platform.Linux;

/// <summary>
///     For sessions that do not tell which window is in front: Wayland keeps that from programs on purpose.
/// </summary>
internal sealed class UnknownTerminalFocus : ITerminalFocus
{
    /// <inheritdoc/>
    public bool? IsFocused() => null;
}