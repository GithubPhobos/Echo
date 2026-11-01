namespace Echo.Updates;

/// <summary>
///     Reads and compares version numbers the way releases are named: "v2.0.10" is "2.0.10", and newer than "2.0.9".
/// </summary>
internal static class ReleaseVersion
{
    /// <summary>
    ///     The version a release tag or a version text names, with the "v" in front of it or without; null for anything
    ///     that is not three numbers, such as "2.1.0-beta".
    /// </summary>
    public static Version? Parse(string? text)
    {
        string trimmed = text?.Trim() ?? string.Empty;

        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
            trimmed = trimmed[1..];

        return Version.TryParse(trimmed, out Version? version) && version.Build >= 0 && version.Revision < 0
            ? version
            : null;
    }

    /// <summary>
    ///     Whether <paramref name="candidate"/> is a later version than <paramref name="installed"/>.
    ///     A version that cannot be read is never later.
    /// </summary>
    public static bool IsNewer(string candidate, string installed) =>
        Parse(candidate) is { } later && Parse(installed) is { } current && later > current;
}