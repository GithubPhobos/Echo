using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using Echo.Platform.Windows;
using Echo.Tests.UnitTests;
using Echo.Updates;

namespace Echo.Tests.IntegrationTests;

/// <summary>
///     install.ps1, run by Windows PowerShell against a server that plays GitHub, with a folder of programs of its own.
///     Each test stops the script before it changes the user's PATH: the tests never touch it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsInstallScriptTests : IDisposable
{
    private const string Archive = "echooo-win-x64.zip";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly InstallLayout _layout;
    private readonly WindowsVersionFolders _folders = new();
    private readonly ReleaseServer _server = new();

    public WindowsInstallScriptTests()
    {
        // The script installs into %LOCALAPPDATA%\Programs\echooo
        _layout = new InstallLayout(Path.Combine(_root, "Programs", "echooo"));
    }

    public void Dispose()
    {
        _server.Dispose();

        // .NET cannot delete a junction inside a folder it deletes whole, so the link goes first
        if (Directory.Exists(_root) && new DirectoryInfo(_layout.Current).LinkTarget is not null)
            Directory.Delete(_layout.Current);

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [WindowsFact]
    public async Task Install_AgainWhileAFileOfTheVersionIsInUse_LeavesItAsItWas()
    {
        // Arrange: Echo 2.0.1 is installed, and another program holds one of its files
        _server.Publish("2.0.1", Release());
        string installed = InstallByHand();

        // Act
        string output;

        using (Hold(installed))
            output = await RunAsync("2.0.1");

        // Assert: told, with every file in its place, the link where it was, and nothing left on the way
        Assert.Contains($"a file in {installed} is in use", output.ReplaceLineEndings(string.Empty));
        AssertVersion(installed);
        Assert.Equal(installed, Directory.ResolveLinkTarget(_layout.Current, returnFinalTarget: true)!.FullName);
        Assert.Equal(["2.0.1"], Directory.EnumerateFileSystemEntries(_layout.Versions).Select(entry => Path.GetFileName(entry)));
    }

    [WindowsFact]
    public async Task Install_WhileEchoChangesTheVersions_WaitsUntilItIsDone()
    {
        // Arrange: an Echo that updates itself holds the lock of the versions; a file held by another program then
        // stops the script before it changes anything
        _server.Publish("2.0.1", Release());
        string installed = InstallByHand();
        using InstallLock held = InstallLock.TryTake(_layout)!;
        var output = new StringBuilder();

        // Act
        using (Hold(installed))
        {
            using Process script = Start(output, "2.0.1");

            await WaitUntilAsync(() => Read(output).Contains("Waiting while Echo updates itself"));

            bool isDoneWhileHeld = script.HasExited;

            held.Dispose();
            await script.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

            // Assert: once the lock is free, the script goes on
            Assert.False(isDoneWhileHeld);
            Assert.Contains("is in use", Read(output).ReplaceLineEndings(string.Empty));
        }
    }

    /// <summary>
    ///     The files of the release, as its archive holds them.
    /// </summary>
    private static Dictionary<string, byte[]> Files() => new()
    {
        ["echooo.exe"] = Encoding.UTF8.GetBytes("the program of Echo 2.0.1"),
        ["library.dll"] = Encoding.UTF8.GetBytes("a library"),
        ["Assets/start-recording.wav"] = Encoding.UTF8.GetBytes("a sound")
    };

    private static Dictionary<string, byte[]> Release()
    {
        var archive = new MemoryStream();

        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create))
        {
            foreach ((string name, byte[] contents) in Files())
            {
                using Stream stream = zip.CreateEntry(name).Open();
                stream.Write(contents);
            }
        }

        return new Dictionary<string, byte[]> { [Archive] = archive.ToArray() };
    }

    /// <summary>
    ///     Lays out Echo 2.0.1 as the script installs it, and gives its folder.
    /// </summary>
    private string InstallByHand()
    {
        string installed = _layout.VersionDirectory("2.0.1");

        foreach ((string name, byte[] contents) in Files())
        {
            string file = Path.Combine(installed, name);

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, contents);
        }

        _folders.PointTo(_layout.Current, installed);

        return installed;
    }

    /// <summary>
    ///     Keeps the sound of the version from being deleted or moved, as an antivirus that reads it can.
    /// </summary>
    private static FileStream Hold(string installed) =>
        File.Open(Path.Combine(installed, "Assets", "start-recording.wav"), FileMode.Open, FileAccess.Read, FileShare.Read);

    private static void AssertVersion(string directory)
    {
        foreach ((string name, byte[] contents) in Files())
            Assert.Equal(contents, File.ReadAllBytes(Path.Combine(directory, name)));
    }

    private Process Start(StringBuilder output, params string[] arguments)
    {
        string script = Path.Combine(AppContext.BaseDirectory, "install", "install.ps1");

        var start = new ProcessStartInfo("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, .. arguments])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        start.Environment["LOCALAPPDATA"] = _root;
        start.Environment["ECHOOO_RELEASES"] = _server.Releases;

        var process = new Process { StartInfo = start };

        process.OutputDataReceived += (_, line) => Append(output, line.Data);
        process.ErrorDataReceived += (_, line) => Append(output, line.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private async Task<string> RunAsync(params string[] arguments)
    {
        var output = new StringBuilder();

        using Process script = Start(output, arguments);
        await script.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

        return Read(output);
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
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(30), "The condition did not come true.");
            await Task.Delay(20);
        }
    }
}