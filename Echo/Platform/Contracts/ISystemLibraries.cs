namespace Echo.Platform.Contracts;

/// <summary>
///     The libraries of the system that speech recognition needs besides its own files. Windows has them always;
///     on Linux one of them may be missing, and the recognition library then takes the whole process down
///     instead of failing, so they are looked for first.
/// </summary>
internal interface ISystemLibraries
{
    /// <summary>
    ///     The names of the needed libraries the system does not have.
    /// </summary>
    IReadOnlyList<string> FindMissing();
}