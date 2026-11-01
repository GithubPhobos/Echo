namespace Echo.UI;

/// <summary>
///     What a polled screen wants after a key or a tick.
/// </summary>
internal readonly record struct PollStep<T>(bool IsFinished, UiResult<T> Result)
{
    public static PollStep<T> Continue { get; } = new(false, default);

    public static PollStep<T> Finish(UiResult<T> result) => new(true, result);
}