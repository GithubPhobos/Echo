namespace Echo.Platform.Contracts;

/// <summary>
///     Starts Echo anew in the same terminal, the way this system allows it.
/// </summary>
internal interface IRelaunch
{
    /// <summary>
    ///     Where this process was started through the link to the version in use, runs that version from its own
    ///     folder instead, starting it anew as long as it asks for that, and returns its exit code. Null where this
    ///     process runs Echo itself.
    /// </summary>
    /// <param name="arguments">The arguments this process was started with.</param>
    int? RunFromVersionFolder(IReadOnlyList<string> arguments);

    /// <summary>
    ///     Starts <paramref name="executable"/> in place of the Echo that ends, in the same terminal. Returns the exit
    ///     code to end this process with: of the new start where this process waits for it, or of a start that failed.
    /// </summary>
    int Restart(string executable, IReadOnlyList<string> arguments);
}