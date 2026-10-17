using Spectre.Console;

namespace Echo.UI;

/// <summary>
///     The screen shown when the application starts: the animated logo, the tagline and the version.
/// </summary>
/// <param name="console">The console to draw on.</param>
internal sealed class StartupScreen(IAnsiConsole console)
{
    private readonly IAnsiConsole _console = console;
    private readonly Theme _theme = Theme.Detect(console);

    /// <summary>
    ///     Plays the startup animation. Nothing is written when the output is not a terminal,
    ///     and a still logo is written when the terminal cannot animate.
    /// </summary>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    public async Task PlayAsync(CancellationToken cancellationToken)
    {
        if (!_console.Profile.Capabilities.Interactive)
            return;

        IReadOnlyList<StartupAnimation.Frame> frames = StartupAnimation.BuildFrames();

        if (!_console.Profile.Capabilities.Ansi)
        {
            foreach (string row in StartupAnimation.RenderRows(frames[^1], useMarkup: false, _theme.LevelBars))
                _console.WriteLine(row);

            _console.WriteLine($"  {ApplicationInfo.Tagline} · v{ApplicationInfo.Version}");
            _console.WriteLine();

            return;
        }

        _console.WriteLine();

        await _console.Live(RenderFrame(frames[0], showTagline: false))
            .StartAsync(async context =>
            {
                // Frames are shown by the clock: one that is already overdue is skipped, so the animation
                // lasts exactly as long as its timing says, however slowly the terminal redraws
                var clock = Stopwatch.StartNew();
                long due = 0;

                for (int i = 0; i < frames.Count; i++)
                {
                    bool isLast = i == frames.Count - 1;
                    long nextDue = due + frames[i].DelayMs;

                    if (isLast || nextDue > clock.ElapsedMilliseconds)
                    {
                        context.UpdateTarget(RenderFrame(frames[i], showTagline: isLast));
                        context.Refresh();

                        if (!isLast)
                            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, nextDue - clock.ElapsedMilliseconds)), cancellationToken);
                    }

                    due = nextDue;
                }
            });

        _console.WriteLine();
    }

    private Markup RenderFrame(StartupAnimation.Frame frame, bool showTagline)
    {
        var rows = new List<string>(StartupAnimation.RenderRows(frame, useMarkup: true, _theme.LevelBars))
        {
            // The tagline row is always there, so the screen keeps its height during the animation
            showTagline
                ? $"  [grey]{ApplicationInfo.Tagline} · v{ApplicationInfo.Version}[/]"
                : string.Empty
        };

        return new Markup(string.Join('\n', rows));
    }
}