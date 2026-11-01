namespace Echo.Setup.Contracts;

/// <summary>
///     One step of the setup wizard: a named group of pages. Steps run in the order they are registered,
///     and any step can be run again on its own: its pages then start from the choices already stored
///     in the context.
/// </summary>
internal interface ISetupStep
{
    /// <summary>
    ///     The setup version that introduced the step. After an update, only steps newer than the completed
    ///     setup are shown.
    /// </summary>
    int Version { get; }

    /// <summary>
    ///     The short name shown in the step line, in the current interface language.
    /// </summary>
    string Title { get; }

    /// <summary>
    ///     The pages of the step, in order. The first one is the step itself; the others open only when
    ///     the earlier choices call for them.
    /// </summary>
    IReadOnlyList<SetupPage> Pages { get; }

    /// <summary>
    ///     The choices of this step as rows of the review screen.
    /// </summary>
    IEnumerable<(string Label, string Value)> Summarize(SetupContext context);

    /// <summary>
    ///     A few characters that stand for the choices of this step in the line of steps, such as "ru/ru"
    ///     or "tiny"; null while there is nothing to show.
    /// </summary>
    string? Brief(SetupContext context);
}