namespace Echo.UI;

/// <summary>
///     Where a wizard screen is in the sequence of steps.
/// </summary>
/// <param name="StepIndex">The zero-based step; equal to the number of steps on the review screen.</param>
/// <param name="StepTitles">The titles of all steps, in order.</param>
/// <param name="CanGoBack">Whether Escape leads somewhere; false on the very first screen.</param>
/// <param name="SubTitle">
///     The name of the screen when it is not the first one of its step. It is shown in the frame of the
///     screen for as long as the screen is open; the line of steps above stays the same.
/// </param>
internal sealed record SetupScreen(int StepIndex, IReadOnlyList<string> StepTitles, bool CanGoBack, string? SubTitle = null)
{
    /// <summary>
    ///     Gives the few characters that stand for what was chosen in a step, or null when there is nothing
    ///     to show yet. Asked again on every refresh, so a choice being made shows at once.
    /// </summary>
    public Func<int, string?>? StepValue { get; init; }

    /// <summary>
    ///     What Escape does, for the line of key hints, where it does more than go back; "back" when null.
    /// </summary>
    public string? EscapeHint { get; init; }

    /// <summary>
    ///     Whether the steps after this one are done as well, as they are for a step opened from the review screen.
    /// </summary>
    public bool AreLaterStepsDone { get; init; }

    public bool IsReview => StepIndex >= StepTitles.Count;
}