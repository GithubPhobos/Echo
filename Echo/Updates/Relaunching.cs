namespace Echo.Updates;

/// <summary>
///     How Echo starts itself anew in the same terminal, for /restart: what it exits with, and what the new start is told.
/// </summary>
internal static class Relaunching
{
    /// <summary>
    ///     The exit code that asks for a new start. 75 means "try again" among the exit codes of Unix programs.
    /// </summary>
    public const int ExitCode = 75;

    /// <summary>
    ///     Set for an Echo started by another one that waits for it and starts it anew when it exits with
    ///     <see cref="ExitCode"/>. Windows needs that, since a process there cannot replace itself with another.
    /// </summary>
    public const string LauncherVariable = "ECHOOO_LAUNCHER";

    /// <summary>
    ///     Tells the new start which version it was started from, so it can say that it was updated.
    /// </summary>
    public const string UpdatedFromOption = "--updated-from";

    /// <summary>
    ///     Skips the startup animation: the user has seen it already.
    /// </summary>
    public const string NoBannerOption = "--no-banner";

    /// <summary>
    ///     Whether an Echo above this one starts it anew.
    /// </summary>
    public static bool HasLauncher(Func<string, string?> variable) =>
        !string.IsNullOrEmpty(variable(LauncherVariable));

    /// <summary>
    ///     The arguments of the new start: the ones Echo was started with, without the animation, and with the
    ///     version it is started from.
    /// </summary>
    /// <param name="arguments">The arguments of the start that ends, which may already have the ones added here.</param>
    /// <param name="fromVersion">The version that ends.</param>
    public static IReadOnlyList<string> Arguments(IReadOnlyList<string> arguments, string fromVersion)
    {
        var next = new List<string>();

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];

            if (argument == NoBannerOption || argument.StartsWith(UpdatedFromOption + "=", StringComparison.Ordinal))
                continue;

            // The version that follows the option goes with it
            if (argument == UpdatedFromOption)
            {
                index++;

                continue;
            }

            next.Add(argument);
        }

        return [.. next, NoBannerOption, UpdatedFromOption, fromVersion];
    }
}