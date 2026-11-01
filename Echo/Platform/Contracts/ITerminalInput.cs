namespace Echo.Platform.Contracts;

/// <summary>The working screen's sole input reader: consumes focus records and returns ordinary keys.</summary>
internal interface ITerminalInput : IDisposable
{
    /// <summary>Returns the next available key without waiting, or null when no key arrived.</summary>
    ConsoleKeyInfo? ReadKey();
}