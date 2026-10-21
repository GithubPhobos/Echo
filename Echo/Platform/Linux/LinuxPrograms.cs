using System.Runtime.Versioning;

namespace Echo.Platform.Linux;

/// <summary>
///     The small programs of the system Echo leans on (xsel, wl-copy, tar and the like): whether one is installed,
///     and handing one some text.
/// </summary>
/// <param name="variable">Reads an environment variable.</param>
/// <param name="feedTimeout">How long a program may take over the text; tests give a short time.</param>
[SupportedOSPlatform("linux")]
internal sealed class LinuxPrograms(Func<string, string?> variable, TimeSpan? feedTimeout = null)
{
    private const string Shell = "/bin/sh";

    /// <summary>
    ///     A clipboard program reads the text and leaves a copy of itself running to hold it; the program
    ///     that was started ends at once.
    /// </summary>
    private static readonly TimeSpan _defaultFeedTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<string, string?> _variable = variable;
    private readonly TimeSpan _feedTimeout = feedTimeout ?? _defaultFeedTimeout;

    /// <summary>
    ///     Whether the program is on the search path.
    /// </summary>
    public bool Exists(string program) =>
        Find(program) is not null;

    /// <summary>
    ///     The full path of a program on the search path, or null.
    /// </summary>
    public string? Find(string program) =>
        (_variable("PATH") ?? string.Empty)
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, program))
            .FirstOrDefault(File.Exists);

    /// <summary>
    ///     Runs a program with the text as its input. Whatever it prints is dropped: a copy that stays to hold the
    ///     clipboard would otherwise keep writing into Echo's screen.
    /// </summary>
    /// <param name="program">The program.</param>
    /// <param name="arguments">Its arguments, separated by spaces.</param>
    /// <param name="text">What it gets as input.</param>
    /// <exception cref="IOException">Thrown if the program fails or does not end in time.</exception>
    public async Task FeedAsync(string program, string arguments, string text)
    {
        var start = new ProcessStartInfo(Shell)
        {
            RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            UseShellExecute = false
        };

        // The shell sends the output nowhere and then becomes the program, with the arguments as given
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("exec \"$@\" >/dev/null 2>&1");
        start.ArgumentList.Add(Shell);
        start.ArgumentList.Add(program);

        foreach (string argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start) ?? throw new IOException($"Couldn't start {program}.");

        // One time limit for writing the text and for the end: a program that stops reading would hold the writing
        // forever once the pipe is full
        Task feeding = FeedAndWaitAsync(process, text);

        try
        {
            await feeding.WaitAsync(_feedTimeout);
        }
        catch (TimeoutException)
        {
            // Ending the program ends the writing too; what the writing says then is of no interest
            process.Kill(entireProcessTree: false);
            _ = feeding.ContinueWith(task => task.Exception, TaskScheduler.Default);

            throw new IOException($"{program} did not finish.");
        }

        if (process.ExitCode != 0)
            throw new IOException($"{program} failed with code {process.ExitCode}.");
    }

    /// <summary>
    ///     Hands the text over and waits for the program to end. A program that ends without reading it all makes
    ///     the writing fail with <see cref="IOException"/>.
    /// </summary>
    private static async Task FeedAndWaitAsync(Process process, string text)
    {
        await process.StandardInput.WriteAsync(text);
        process.StandardInput.Close();

        await process.WaitForExitAsync();
    }
}