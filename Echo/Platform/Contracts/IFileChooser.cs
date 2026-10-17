namespace Echo.Platform.Contracts;

/// <summary>
///     The window of the system where the user picks files.
/// </summary>
internal interface IFileChooser
{
    /// <summary>
    ///     Whether the system has such a window.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    ///     Lets the user pick files. Returns their full paths, or nothing when the user closes the window
    ///     without picking.
    /// </summary>
    /// <param name="title">The title of the window.</param>
    /// <param name="pattern">The file name patterns to show, separated by semicolons, e.g. "*.bin".</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    Task<IReadOnlyList<string>> PickAsync(string title, string pattern, CancellationToken cancellationToken);
}