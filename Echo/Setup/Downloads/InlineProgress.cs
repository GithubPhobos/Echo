namespace Echo.Setup.Downloads;

/// <summary>
///     Reports progress on the calling thread, in order. The framework's own implementation posts every
///     update to the thread pool, where updates can overtake each other.
/// </summary>
/// <param name="handler">Called for every update.</param>
internal sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    private readonly Action<T> _handler = handler;

    /// <inheritdoc/>
    public void Report(T value) => _handler(value);
}