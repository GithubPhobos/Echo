using System.Runtime.Versioning;

namespace Echo.Platform.Linux;

/// <summary>
///     The file chooser of the desktop: through its permission window when the desktop has one (FileChooser
///     portal), otherwise through zenity or kdialog, the choosers found on most desktops.
/// </summary>
/// <param name="portal">The permission windows of the desktop; none in a session without a desktop.</param>
/// <param name="programs">Finds the choosers.</param>
/// <param name="processRunner">Runs them.</param>
[SupportedOSPlatform("linux")]
internal sealed class LinuxFileChooser(DesktopPortal? portal, LinuxPrograms programs, IProcessRunner processRunner) : IFileChooser
{
    /// <summary>
    ///     A person may take a long time to find a file; the chooser is not hurried.
    /// </summary>
    private static readonly TimeSpan _chooserTimeout = TimeSpan.FromHours(1);

    private readonly DesktopPortal? _portal = portal;
    private readonly LinuxPrograms _programs = programs;
    private readonly IProcessRunner _processRunner = processRunner;

    private bool? _hasPortal;

    /// <inheritdoc/>
    public bool IsAvailable => HasPortal() || _programs.Exists("zenity") || _programs.Exists("kdialog");

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> PickAsync(string title, string pattern, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> patterns = Patterns(pattern);

        if (HasPortal())
            return await _portal!.OpenFilesAsync(title, patterns, cancellationToken);

        // Both choosers want several patterns separated by spaces
        string filter = string.Join(' ', patterns);

        // The first separates paths with a bar, the second prints one per line
        string? output =
            await _processRunner.RunAsync(
                "zenity",
                $"--file-selection --multiple --title=\"{title}\" --file-filter=\"{filter}\"",
                _chooserTimeout,
                cancellationToken)
            ?? await _processRunner.RunAsync(
                "kdialog",
                $"--getopenfilename . \"{filter}\" --multiple --separate-output --title \"{title}\"",
                _chooserTimeout,
                cancellationToken);

        return [.. (output ?? string.Empty).Split(['\n', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    /// <summary>
    ///     The patterns of the caller, separated by semicolons. A plain name also matches the file of the same
    ///     name with a version after it: on Linux 'libcublas.so.13' is often a link to 'libcublas.so.13.8.0.4'.
    /// </summary>
    internal static IReadOnlyList<string> Patterns(string pattern) =>
    [
        .. pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(single => single.Contains('*') ? single : single + "*")
    ];

    private bool HasPortal()
    {
        if (_portal is null)
            return false;

        // Asked once: the answer does not change while Echo runs
        _hasPortal ??= _portal.VersionAsync(DesktopPortal.FileChooserInterface).GetAwaiter().GetResult() is not null;

        return _hasPortal.Value;
    }
}