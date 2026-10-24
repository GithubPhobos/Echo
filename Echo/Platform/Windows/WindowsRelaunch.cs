using System.ComponentModel;
using System.Runtime.Versioning;
using Echo.Updates;

namespace Echo.Platform.Windows;

/// <inheritdoc cref="IRelaunch"/>
/// <remarks>
///     A Windows process cannot replace itself with another. So the Echo that the user starts only launches Echo,
///     in the same console, and waits for it: when it exits asking for a new start, it launches it again. It holds
///     nothing but its own files meanwhile. Ctrl+C belongs to the Echo it launched, so it does not end the launcher.
///
///     The launcher also keeps a running Echo in its version folder: the command runs through the junction "current",
///     and a program started through a junction sees the junction's path as its own, so it would load files of the
///     next version once the junction is switched. The launcher starts Echo from the folder the junction leads to.
/// </remarks>
/// <param name="run">Starts a program with arguments and waits for its exit code; a real process when not given.</param>
[SupportedOSPlatform("windows")]
internal sealed class WindowsRelaunch(Func<string, IReadOnlyList<string>, int>? run = null) : IRelaunch
{
    private readonly Func<string, IReadOnlyList<string>, int> _run = run ?? RunAndWait;

    /// <inheritdoc/>
    public int? RunFromVersionFolder(IReadOnlyList<string> arguments)
    {
        if (Environment.ProcessPath is not { } program || ResolveLink(program) == program)
            return null;

        return Launch(program, arguments);
    }

    /// <inheritdoc/>
    public int Restart(string executable, IReadOnlyList<string> arguments) =>
        Launch(executable, arguments);

    /// <summary>
    ///     Runs Echo from its version folder, and again as long as it exits asking for a new start.
    /// </summary>
    internal int Launch(string executable, IReadOnlyList<string> arguments)
    {
        Console.CancelKeyPress += (_, args) => args.Cancel = true;

        while (true)
        {
            string program = ResolveLink(executable);

            int exitCode;

            try
            {
                exitCode = _run(program, arguments);
            }
            catch (Win32Exception exc)
            {
                Console.Error.WriteLine(Strings.Format(nameof(Strings.RestartFailed), exc.Message));

                return 1;
            }

            if (exitCode != Relaunching.ExitCode)
                return exitCode;

            arguments = Relaunching.Arguments(arguments, VersionOf(program));
        }
    }

    /// <summary>
    ///     The program file in the folder its folder leads to, when the folder is a link; the same file otherwise.
    /// </summary>
    internal static string ResolveLink(string program)
    {
        string folder = Path.GetDirectoryName(program)!;

        try
        {
            return Directory.ResolveLinkTarget(folder, returnFinalTarget: true) is { } target
                ? Path.Combine(target.FullName, Path.GetFileName(program))
                : program;
        }
        catch (IOException)
        {
            return program;
        }
    }

    /// <summary>
    ///     The version a program file belongs to: the name of its folder in the install layout, or this one's version.
    /// </summary>
    internal static string VersionOf(string program) =>
        InstallLayout.Of(Path.GetDirectoryName(program)!) is not null
            ? Path.GetFileName(Path.GetDirectoryName(program)!)
            : ApplicationInfo.Version;

    private static int RunAndWait(string program, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false };

        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        start.Environment[Relaunching.LauncherVariable] = "1";

        using Process process = Process.Start(start)!;

        process.WaitForExit();

        return process.ExitCode;
    }
}