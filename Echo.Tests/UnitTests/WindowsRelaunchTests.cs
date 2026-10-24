using System.Runtime.Versioning;
using Echo.Constants;
using Echo.Platform.Windows;
using Echo.Updates;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class WindowsRelaunchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    public WindowsRelaunchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        // .NET cannot delete a junction inside a folder it deletes whole, so the link goes first
        string link = Path.Combine(_root, "current");

        if (new DirectoryInfo(link).LinkTarget is not null)
            Directory.Delete(link);

        Directory.Delete(_root, recursive: true);
    }

    [WindowsFact]
    public void Launch_ThroughTheLink_RunsTheProgramOfTheVersionFolderAndAgainAfterRestart()
    {
        // Arrange: the installed layout, with the link at 2.0.1
        var layout = new InstallLayout(_root);
        var folders = new WindowsVersionFolders();

        Directory.CreateDirectory(layout.VersionDirectory("2.0.1"));
        Directory.CreateDirectory(layout.VersionDirectory("2.0.2"));
        folders.PointTo(layout.Current, layout.VersionDirectory("2.0.1"));

        var runs = new List<(string Program, IReadOnlyList<string> Arguments)>();

        // The first run downloads 2.0.2, which switches the link, and asks for a new start; the second one ends
        var relaunch = new WindowsRelaunch((program, arguments) =>
        {
            runs.Add((program, arguments));

            if (runs.Count > 1)
                return 3;

            folders.PointTo(layout.Current, layout.VersionDirectory("2.0.2"));

            return Relaunching.ExitCode;
        });

        // Act
        int exitCode = relaunch.Launch(Path.Combine(layout.Current, "echooo.exe"), ["listen"]);

        // Assert: always from the version folder itself, and the new start is told where it came from
        Assert.Equal(3, exitCode);
        Assert.Equal(2, runs.Count);
        Assert.Equal(Path.Combine(layout.VersionDirectory("2.0.1"), "echooo.exe"), runs[0].Program);
        Assert.Equal(["listen"], runs[0].Arguments);
        Assert.Equal(Path.Combine(layout.VersionDirectory("2.0.2"), "echooo.exe"), runs[1].Program);
        Assert.Equal(["listen", "--no-banner", "--updated-from", "2.0.1"], runs[1].Arguments);
    }

    [WindowsFact]
    public void Launch_ProgramOutsideTheLayout_RestartsItselfWithItsOwnVersion()
    {
        // Arrange: a developer's build
        string program = Path.Combine(_root, "bin", "echooo.exe");
        var runs = new List<IReadOnlyList<string>>();

        var relaunch = new WindowsRelaunch((_, arguments) =>
        {
            runs.Add(arguments);

            return runs.Count == 1 ? Relaunching.ExitCode : 0;
        });

        // Act
        int exitCode = relaunch.Launch(program, []);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.Equal(["--no-banner", "--updated-from", ApplicationInfo.Version], runs[1]);
    }

    [WindowsFact]
    public void ResolveLink_ProgramInARealFolder_StaysAsItIs()
    {
        // Arrange
        string program = Path.Combine(_root, "echooo.exe");

        // Act & Assert
        Assert.Equal(program, WindowsRelaunch.ResolveLink(program));
    }
}