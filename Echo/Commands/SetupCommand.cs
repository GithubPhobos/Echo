using System.ComponentModel;
using Echo.Setup;
using Echo.UI;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Echo.Commands;

/// <summary>
///     Runs the setup wizard again, with the current choices preselected.
/// </summary>
/// <param name="console">The console to draw on.</param>
/// <param name="setupRunner"><see cref="SetupRunner"/></param>
internal sealed class SetupCommand(IAnsiConsole console, SetupRunner setupRunner) : AsyncCommand<SetupCommand.SetupSettings>
{
    internal const string Name = "setup";
    internal const string Description = "Runs the setup wizard.";

    /// <summary>
    ///     Returned when the wizard is needed but there is no terminal to show it in.
    /// </summary>
    internal const int NoTerminalExitCode = 2;

    private readonly IAnsiConsole _console = console;
    private readonly SetupRunner _setupRunner = setupRunner;

    /// <summary>
    ///     Command line options of <see cref="SetupCommand"/>.
    /// </summary>
    internal sealed class SetupSettings : CommandSettings
    {
        [CommandOption("--no-banner")]
        [Description("Skips the startup animation.")]
        public bool NoBanner { get; init; }
    }

    /// <inheritdoc/>
    public override async Task<int> ExecuteAsync(CommandContext context,
                                                 SetupSettings settings,
                                                 CancellationToken cancellationToken)
    {
        if (!_console.Profile.Capabilities.Interactive)
        {
            _console.WriteLine(Strings.SetupNeedsTerminal);

            return NoTerminalExitCode;
        }

        if (!settings.NoBanner)
            await new StartupScreen(_console).PlayAsync(cancellationToken);

        await _setupRunner.RunAsync(onlyNewSteps: false, cancellationToken);

        _console.MarkupLine($"  [{Theme.Success}]{Markup.Escape(Strings.SetupSaved)}[/]");
        _console.WriteLine();

        return 0;
    }
}