namespace Echo.Tests.UnitTests;

/// <summary>
///     A test of Linux code that runs only on Linux and only with what it needs there; anywhere else it is
///     counted as skipped, not as passed.
/// </summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    /// <param name="needs">
    ///     Programs the test runs, looked for on the search path, or files it uses, by their full paths.
    /// </param>
    public LinuxFactAttribute(params string[] needs)
    {
        Skip = WhyNot(needs);
    }

    /// <summary>
    ///     Why the test cannot run on this machine, or null when it can.
    /// </summary>
    internal static string? WhyNot(IReadOnlyList<string> needs)
    {
        if (!OperatingSystem.IsLinux())
            return "Linux only.";

        string[] path = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries);

        string? missing = needs.FirstOrDefault(need => need.Contains('/')
            ? !File.Exists(need)
            : !path.Any(directory => File.Exists(Path.Combine(directory, need))));

        return missing is null ? null : $"Needs {missing}.";
    }
}