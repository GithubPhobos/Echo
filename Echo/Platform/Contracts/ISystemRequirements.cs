namespace Echo.Platform.Contracts;

/// <summary>
///     What Echo needs from the system besides itself: programs and parts of the desktop that are not installed
///     everywhere. Windows has all of it; on Linux it depends on the distribution and the desktop.
/// </summary>
internal interface ISystemRequirements
{
    /// <summary>
    ///     Looks at the system. Takes a moment: it asks the sound server and the desktop.
    /// </summary>
    IReadOnlyList<SystemRequirement> Check();

    /// <summary>
    ///     The command line that installs the packages, the program first; null where Echo cannot install them.
    /// </summary>
    IReadOnlyList<string>? InstallCommand(IReadOnlyList<string> packages);
}