using Spectre.Console;

namespace Echo.Platform;

/// <summary>The existing key-only input for redirected streams, unsupported consoles and scripted screens.</summary>
/// <param name="input">The console input already in use.</param>
internal sealed class ConsoleTerminalInput(IAnsiConsoleInput input) : ITerminalInput
{
    /// <inheritdoc/>
    public ConsoleKeyInfo? ReadKey() => input.IsKeyAvailable() ? input.ReadKey(intercept: true) : null;

    /// <inheritdoc/>
    public void Dispose() { }
}