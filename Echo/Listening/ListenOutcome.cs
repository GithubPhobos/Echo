namespace Echo.Listening;

/// <summary>
///     What the working screen does after a command.
/// </summary>
internal enum ListenOutcome
{
    /// <summary>Goes on working.</summary>
    Continue,

    /// <summary>Leaves the screen for the setup wizard and comes back with the new settings.</summary>
    Setup,

    /// <summary>Leaves Echo.</summary>
    Quit,

    /// <summary>Leaves Echo and starts it anew in the same terminal, in the newer version when one is downloaded.</summary>
    Restart
}