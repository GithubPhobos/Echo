using System.Collections.Concurrent;
using SharpHook;
using SharpHook.Data;

namespace Echo.Setup.Hotkeys;

/// <inheritdoc cref="IHotkeyCaptureFactory"/>
internal sealed class HotkeyCaptureFactory : IHotkeyCaptureFactory
{
    /// <inheritdoc/>
    public IHotkeyCapture Start() => new HotkeyCapture();

    /// <summary>
    ///     Uses the same global keyboard hook as dictation itself, so whatever is captured here
    ///     is exactly what will be recognized later. The hook hears the whole system, so keys pressed
    ///     while the user is in another window are left out.
    /// </summary>
    private sealed class HotkeyCapture : IHotkeyCapture
    {
        private readonly SimpleGlobalHook _hook = new();
        private readonly ConcurrentQueue<HotkeyChoice> _pressed = new();

        public HotkeyCapture()
        {
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
            if (TerminalFocus.IsFocused() == false)
                return;

            _pressed.Enqueue(HotkeyNames.ToChoice(e.Data.KeyCode, e.RawEvent.Mask));
        }
    }
}