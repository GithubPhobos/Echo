namespace Echo.Setup;

/// <summary>
///     How a run of the setup wizard ended.
/// </summary>
internal enum SetupOutcome
{
    Completed,
    NothingToDo,

    /// <summary>
    ///     The review screen of a completed setup was left without saving.
    /// </summary>
    Cancelled
}