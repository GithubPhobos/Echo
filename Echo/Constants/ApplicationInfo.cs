using System.Reflection;

namespace Echo.Constants;

/// <summary>
///     Application identity shown in the command line interface.
/// </summary>
internal static class ApplicationInfo
{
    /// <summary>
    ///     The name of the executable/command. 'echo' itself is a shell builtin, hence the trailing echo.
    /// </summary>
    public const string CommandName = "echooo";

    public const string Tagline = "push-to-talk voice typing";

    /// <summary>
    ///     The application version in 'major.minor.patch' format.
    /// </summary>
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}