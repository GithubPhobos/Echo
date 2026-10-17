namespace Echo.Infrastructure;

/// <inheritdoc cref="ISystemShell"/>
/// <param name="processRunner">Runs the file chooser programs on Linux.</param>
internal sealed class SystemShell(IProcessRunner processRunner) : ISystemShell
{
    /// <summary>
    ///     A person may take a long time to find a file; the chooser is not hurried.
    /// </summary>
    private static readonly TimeSpan _chooserTimeout = TimeSpan.FromHours(1);

    private readonly IProcessRunner _processRunner = processRunner;

    /// <inheritdoc/>
    public bool CanPickFiles =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <inheritdoc/>
    public long GetFreeSpace(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));

            return string.IsNullOrEmpty(root) ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception exc) when (exc is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Not knowing is no reason to refuse a download; a full disk will fail it with a clear error anyway
            return long.MaxValue;
        }
    }

    /// <inheritdoc/>
    public void CreateDirectory(string path) =>
        Directory.CreateDirectory(path);

    /// <inheritdoc/>
    public void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exc) when (exc is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            // The path is on the screen as well, so a missing file manager is not a problem
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> PickFilesAsync(string title, string pattern, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
            return await WindowsFileChooser.ShowAsync(title, pattern);

        // Both choosers want several patterns separated by spaces
        string patterns = pattern.Replace(';', ' ');

        // The two choosers found on nearly every Linux desktop. The first separates paths with a bar,
        // the second prints one per line
        string? output =
            await _processRunner.RunAsync(
                "zenity",
                $"--file-selection --multiple --title=\"{title}\" --file-filter=\"{patterns}\"",
                _chooserTimeout,
                cancellationToken)
            ?? await _processRunner.RunAsync(
                "kdialog",
                $"--getopenfilename . \"{patterns}\" --multiple --separate-output --title \"{title}\"",
                _chooserTimeout,
                cancellationToken);

        return [.. (output ?? string.Empty).Split(['\n', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}