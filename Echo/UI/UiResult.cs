namespace Echo.UI;

/// <summary>
///     What the user did on a screen.
/// </summary>
/// <param name="IsBack">Whether the user asked to go back.</param>
/// <param name="Value">The outcome otherwise.</param>
internal readonly record struct UiResult<T>(bool IsBack, T Value)
{
    public static UiResult<T> Back { get; } = new(true, default!);

    public static UiResult<T> Done(T value) => new(false, value);
}