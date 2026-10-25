using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using Echo.Tests.UnitTests;
using Echo.Updates;

namespace Echo.Tests.IntegrationTests;

/// <summary>
///     install.sh, run as "curl ... | sh" runs it, against a server that plays GitHub, with a home folder of its own.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxInstallScriptTests : IDisposable
{
    private const string Archive = "echooo-linux-x64.tar.gz";

    /// <summary>
    ///     The programs the script runs besides the one it downloads with.
    /// </summary>
    private static readonly string[] _programs =
        ["uname", "tar", "gzip", "sha256sum", "grep", "sed", "tail", "tr", "mktemp", "rm", "mv", "mkdir", "ln", "flock"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly string _home;
    private readonly ReleaseServer _server = new();

    public LinuxInstallScriptTests()
    {
        _home = Path.Combine(_root, "home");
        Directory.CreateDirectory(_home);
    }

    public void Dispose()
    {
        _server.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [LinuxFact("wget")]
    public async Task Install_LatestWithWget_InstallsIt()
    {
        // Arrange: a system without curl, as a fresh Ubuntu desktop
        _server.Publish("2.0.1", Release("2.0.1"));

        // Act
        (int exitCode, string output) = await RunAsync(Tools("wget"));

        // Assert: the command runs the latest version
        Assert.True(exitCode == 0, output);
        Assert.Contains("Echo 2.0.1 is installed.", output);
        Assert.Equal("2.0.1", await RunInstalledAsync());
    }

    [LinuxFact("curl")]
    public async Task Install_LatestWithCurl_InstallsIt()
    {
        // Arrange
        _server.Publish("2.0.1", Release("2.0.1"));

        // Act
        (int exitCode, string output) = await RunAsync(Tools("curl"));

        // Assert
        Assert.True(exitCode == 0, output);
        Assert.Equal("2.0.1", await RunInstalledAsync());
    }

    [LinuxFact("wget")]
    public async Task Install_SameVersionAgain_ReplacesIt()
    {
        // Arrange
        _server.Publish("2.0.1", Release("2.0.1"));
        await RunAsync(Tools("wget"));

        // Act
        (int exitCode, string output) = await RunAsync(Tools("wget"), "2.0.1");

        // Assert: one folder of the version, and nothing set aside on the way
        Assert.True(exitCode == 0, output);
        Assert.Equal("2.0.1", await RunInstalledAsync());
        Assert.Equal(["2.0.1"], Directory.EnumerateFileSystemEntries(Layout().Versions).Select(entry => Path.GetFileName(entry)));
    }

    [LinuxFact("wget", "flock")]
    public async Task Install_WhileEchoChangesTheVersions_WaitsUntilItIsDone()
    {
        // Arrange: an Echo that updates itself holds the lock of the versions
        _server.Publish("2.0.1", Release("2.0.1"));

        InstallLayout layout = Layout();
        Directory.CreateDirectory(layout.Root);
        using InstallLock held = InstallLock.TryTake(layout)!;

        var output = new StringBuilder();

        // Act
        using Process script = Start(Tools("wget"), output);

        await WaitUntilAsync(() => Read(output).Contains("Waiting while Echo updates itself"));

        bool isInstalledWhileHeld = Directory.Exists(layout.Current);

        held.Dispose();
        await script.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        // Assert
        Assert.False(isInstalledWhileHeld);
        Assert.True(script.ExitCode == 0, Read(output));
        Assert.Equal("2.0.1", await RunInstalledAsync());
    }

    [LinuxFact("wget")]
    public async Task Install_NotAVersion_SaysSo()
    {
        // Arrange
        _server.Publish("2.0.1", Release("2.0.1"));

        // Act
        (int exitCode, string output) = await RunAsync(Tools("wget"), "2.0.1/../../x");

        // Assert
        Assert.Equal(1, exitCode);
        Assert.Contains("'2.0.1/../../x' is not a version of Echo", output);
    }

    /// <summary>
    ///     The release of this system: the program is a script that tells its version, as the archive the release
    ///     builds with "tar -C out -czf ... ." holds it.
    /// </summary>
    private static Dictionary<string, byte[]> Release(string version)
    {
        var archive = new MemoryStream();

        using (var gzip = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "./echooo")
            {
                Mode = (UnixFileMode)0b111_101_101,
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes($"#!/bin/sh\necho {version}\n"))
            });
        }

        return new Dictionary<string, byte[]> { [Archive] = archive.ToArray() };
    }

    /// <summary>
    ///     Where the script installs Echo for the home folder of the test.
    /// </summary>
    private InstallLayout Layout() => new(Path.Combine(_home, ".local", "share", "echooo"));

    /// <summary>
    ///     A folder of links to the programs the script may run, with <paramref name="downloader"/> as the only one it
    ///     can download with: the search path of the script, which leaves out the other.
    /// </summary>
    private string Tools(string downloader)
    {
        string tools = Path.Combine(_root, "tools-" + downloader);

        if (Directory.Exists(tools))
            return tools;

        Directory.CreateDirectory(tools);

        string[] path = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries);

        foreach (string program in _programs.Append(downloader))
        {
            if (path.Select(directory => Path.Combine(directory, program)).FirstOrDefault(File.Exists) is { } found)
                File.CreateSymbolicLink(Path.Combine(tools, program), found);
        }

        return tools;
    }

    private Process Start(string tools, StringBuilder output, params string[] arguments)
    {
        var start = new ProcessStartInfo("/bin/sh", [Path.Combine(AppContext.BaseDirectory, "install", "install.sh"), .. arguments])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        start.Environment["PATH"] = tools;
        start.Environment["HOME"] = _home;
        start.Environment["ECHOOO_RELEASES"] = _server.Releases;
        start.Environment.Remove("XDG_DATA_HOME");

        var script = new Process { StartInfo = start };

        script.OutputDataReceived += (_, line) => Append(output, line.Data);
        script.ErrorDataReceived += (_, line) => Append(output, line.Data);

        script.Start();
        script.BeginOutputReadLine();
        script.BeginErrorReadLine();

        return script;
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string tools, params string[] arguments)
    {
        var output = new StringBuilder();

        using Process script = Start(tools, output, arguments);
        await script.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        return (script.ExitCode, Read(output));
    }

    /// <summary>
    ///     Runs the command the script put in ~/.local/bin, and gives what it printed.
    /// </summary>
    private async Task<string> RunInstalledAsync()
    {
        using Process program = Process.Start(new ProcessStartInfo(Path.Combine(_home, ".local", "bin", "echooo"))
        {
            RedirectStandardOutput = true
        })!;

        string output = await program.StandardOutput.ReadToEndAsync();
        await program.WaitForExitAsync();

        return output.Trim();
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null)
            return;

        lock (output)
            output.AppendLine(line);
    }

    private static string Read(StringBuilder output)
    {
        lock (output)
            return output.ToString();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(20), "The condition did not come true.");
            await Task.Delay(20);
        }
    }
}