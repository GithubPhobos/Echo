using System.Runtime.Versioning;
using SharpHook;
using SharpHook.Data;

namespace Echo.Platform.Linux;

/// <summary>
///     Presses Shift+Insert as if the user did, through the XTest part of X11. Unlike Ctrl+V it pastes in the
///     terminals as well as in ordinary windows; which of the two clipboards each program takes is covered by
///     the text being in both.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class X11PasteKeys : IPasteKeys
{
    private readonly EventSimulator _simulator = new();

    /// <summary>
    ///     X11 lets any program press keys: there is nothing to renew.
    /// </summary>
    /// <inheritdoc/>
    public Task PrepareAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<bool> PasteAsync()
    {
        bool isPressed =
            _simulator.SimulateKeyPress(KeyCode.VcLeftShift) == UioHookResult.Success
            && _simulator.SimulateKeyPress(KeyCode.VcInsert) == UioHookResult.Success;

        // Released in any case, so no key stays down for the system
        _simulator.SimulateKeyRelease(KeyCode.VcInsert);
        _simulator.SimulateKeyRelease(KeyCode.VcLeftShift);

        return Task.FromResult(isPressed);
    }
}