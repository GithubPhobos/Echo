using GregsStack.InputSimulatorStandard.Native;

namespace Echo.Platform.Windows;

/// <summary>
///     Pastes with Ctrl+V, which every program on Windows understands, the terminals included.
/// </summary>
/// <param name="inputSimulator">Presses keys as if the user did.</param>
[SupportedOSPlatform("windows")]
internal sealed class CtrlVPasteKeys(IInputSimulator inputSimulator) : IPasteKeys
{
    private readonly IInputSimulator _inputSimulator = inputSimulator;

    /// <summary>
    ///     Windows lets any program press keys: there is nothing to renew.
    /// </summary>
    /// <inheritdoc/>
    public Task PrepareAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<bool> PasteAsync()
    {
        _inputSimulator.Keyboard.ModifiedKeyStroke(
            VirtualKeyCode.CONTROL,
            VirtualKeyCode.VK_V);

        return Task.FromResult(true);
    }
}