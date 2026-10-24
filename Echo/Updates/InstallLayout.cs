namespace Echo.Updates;

/// <summary>
///     The folders the install script puts Echo in, the same on every system: every version in a folder of its own
///     under "versions", and the link "current" to the one in use. Switching to another version is pointing the link
///     at it; a running Echo keeps its own folder, so the switch never changes files under it.
/// </summary>
/// <param name="root">The folder that holds "versions" and "current".</param>
internal sealed class InstallLayout(string root)
{
    public const string VersionsName = "versions";
    public const string CurrentName = "current";

    /// <summary>
    ///     Ends the name of the folder a version is downloaded into, before it is complete.
    /// </summary>
    private const string PartialSuffix = ".part";

    public string Root { get; } = root;

    public string Versions => Path.Combine(Root, VersionsName);

    /// <summary>
    ///     The link to the version in use: the one Echo starts from.
    /// </summary>
    public string Current => Path.Combine(Root, CurrentName);

    public string VersionDirectory(string version) => Path.Combine(Versions, version);

    /// <summary>
    ///     Where the archive of a version is downloaded to.
    /// </summary>
    public string PartialDirectory(string version) => Path.Combine(Versions, version + PartialSuffix);

    /// <summary>
    ///     The layout of an Echo that runs from <paramref name="directory"/>, when the install script put it there:
    ///     its folder is "&lt;root&gt;/versions/&lt;version&gt;" and "&lt;root&gt;/current" is there. Null for an
    ///     Echo unpacked by hand or built by a developer, which cannot update itself.
    /// </summary>
    public static InstallLayout? Of(string directory)
    {
        string own = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

        if (Path.GetDirectoryName(own) is not { } versions
            || !string.Equals(Path.GetFileName(versions), VersionsName, PathNames.Comparison)
            || Path.GetDirectoryName(versions) is not { } root)
        {
            return null;
        }

        return Directory.Exists(Path.Combine(root, CurrentName)) ? new InstallLayout(root) : null;
    }

    /// <summary>
    ///     The folder <see cref="Current"/> leads to, or null when there is none.
    /// </summary>
    public string? CurrentDirectory()
    {
        if (!Directory.Exists(Current))
            return null;

        try
        {
            return Directory.ResolveLinkTarget(Current, returnFinalTarget: true)?.FullName ?? Current;
        }
        catch (IOException)
        {
            return null;
        }
    }
}