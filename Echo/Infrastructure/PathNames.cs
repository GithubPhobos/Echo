namespace Echo.Infrastructure;

/// <summary>
///     How the file system of this system compares names: Windows does not tell capital letters from small
///     ones, Linux does, so "Model.bin" and "model.bin" are two different files there.
/// </summary>
internal static class PathNames
{
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    ///     Whether two paths lead to the same place.
    /// </summary>
    public static bool AreSame(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), Comparison);

    /// <summary>
    ///     Whether <paramref name="path"/> is <paramref name="directory"/> or lies somewhere inside it.
    /// </summary>
    public static bool IsInside(string path, string directory)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetFullPath(directory);

        if (string.Equals(full, root, Comparison))
            return true;

        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;

        return full.StartsWith(prefix, Comparison);
    }
}