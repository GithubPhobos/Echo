namespace Echo.Listening;

/// <summary>
///     What a line of the working screen's log is.
/// </summary>
internal enum EntryKind
{
    /// <summary>An ordinary message of the program.</summary>
    Message,

    /// <summary>Something that went wrong but did not stop Echo.</summary>
    Warning,

    /// <summary>Something that failed.</summary>
    Error,

    /// <summary>A phrase the user dictated, as it was recognized.</summary>
    Dictated,

    /// <summary>A command the user typed.</summary>
    Command,

    /// <summary>What a command answered.</summary>
    Answer
}