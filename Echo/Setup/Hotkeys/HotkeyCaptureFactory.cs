namespace Echo.Setup.Hotkeys;

/// <inheritdoc cref="IHotkeyCaptureFactory"/>
/// <param name="terminalFocus">Tells whether the user is typing into the wizard or into another window.</param>
internal sealed class HotkeyCaptureFactory(ITerminalFocus terminalFocus) : IHotkeyCaptureFactory
{
    private readonly ITerminalFocus _terminalFocus = terminalFocus;

    /// <inheritdoc/>
    public IHotkeyCapture Start() => new HotkeyCapture(_terminalFocus);

    /// <summary>
    ///     Hears the keyboard of the whole system through a global keyboard hook, on Windows and on Linux with X11,
    ///     so keys pressed while the user is in another window are left out.
    /// </summary>
    private sealed class HotkeyCapture : IHotkeyCapture
    {
        private readonly SimpleGlobalHook _hook = new();
        private readonly ConcurrentQueue<HotkeyChoice> _pressed = new();
        private readonly ITerminalFocus _terminalFocus;

        public HotkeyCapture(ITerminalFocus terminalFocus)
        {
            _terminalFocus = terminalFocus;
            _hook.KeyPressed += OnKeyPressed;

            _ = _hook.RunAsync();
        }

        public bool TryRead(out HotkeyChoice choice) =>
            _pressed.TryDequeue(out choice!);

        public void Dispose()
        {
            _hook.KeyPressed -= OnKeyPressed;
            _hook.Dispose();
        }

        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode is KeyCode.VcEnter or KeyCode.VcNumPadEnter or KeyCode.VcEscape or KeyCode.VcUndefined)
                return;

            // A key pressed in another window is not the user's choice; when that cannot be told, the key is heard
            if (_terminalFocus.IsFocused() == false)
                return;

            _pressed.Enqueue(HotkeyNames.ToChoice(e.Data.KeyCode, e.RawEvent.Mask));
        }
    }
}