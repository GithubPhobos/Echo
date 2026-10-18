namespace Echo.Infrastructure.Contracts;

/// <summary>
///     The few things the wizard asks of the operating system besides reading and writing its own files.
/// </summary>
internal interface ISystemShell
{
    /// <summary>
    ///     Free space on the drive that holds <paramref name="path"/>.
    /// </summary>
    long GetFreeSpace(string path);

    void CreateDirectory(string path);

    /// <summary>
    ///     Opens the folder in the file manager, so the user can drop files into it.
    /// </summary>
    void OpenFolder(string path);

    /// <summary>
    ///     Whether <see cref="PickFilesAsync"/> can show a file chooser on this system.
    /// </summary>
    bool CanPickFiles { get; }

    /// <summary>
    ///     Lets the user pick files in the file chooser of the operating system.
    ///     Returns nothing when the user closes the chooser without picking.
    /// </summary>
    /// <param name="title">The title of the chooser window.</param>
    /// <param name="pattern">The file name patterns to show, separated by semicolons, e.g. "*.bin".</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    Task<IReadOnlyList<string>> PickFilesAsync(string title, string pattern, CancellationToken cancellationToken);

    /// <summary>
    ///     Runs a program in this terminal and waits for it: it writes to the screen and reads the keyboard itself,
    ///     as when the user typed the command, so it can ask for a password.
    /// </summary>
    /// <param name="command">The program, then its arguments.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    /// <returns>Whether the program ran and succeeded.</returns>
    Task<bool> RunInTerminalAsync(IReadOnlyList<string> command, CancellationToken cancellationToken);
}