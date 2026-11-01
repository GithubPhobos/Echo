namespace Echo.Platform.Linux;

/// <summary>
///     For sessions without windows, such as a text console or a remote shell: there is no keyboard of a desktop
///     to listen to.
/// </summary>
internal sealed class NoHotkeyListener : IHotkeyListener
{
    /// <inheritdoc/>
    public event EventHandler<HotkeyEventArgs>? Pressed
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public event EventHandler<HotkeyEventArgs>? Released
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public event EventHandler? Lost
    {
        add { }
        remove { }
    }

    /// <inheritdoc/>
    public void Bind(HotkeyChoice hotkey) =>
        throw new Exception("The push-to-talk key needs a desktop session (X11 or Wayland); this one has none.");

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task ReleaseAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}