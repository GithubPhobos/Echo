using Echo.Commands;

IRelaunch relaunch = PlatformServiceCollectionExtensions.CreateRelaunch();

// The installed Echo on Windows is started through the link to the version in use, and runs that version from
// its own folder instead, so that the link can be switched to a newer version under it
if (relaunch.RunFromVersionFolder(args) is int launched)
    return launched;

Console.OutputEncoding = Encoding.UTF8;

// Ctrl+C in the middle of a live screen must not leave the terminal without a cursor
Console.CancelKeyPress += (_, _) => Spectre.Console.AnsiConsole.Cursor.Show(true);

AppPaths paths = AppPaths.CreateDefault();

var services = new ServiceCollection();
services.RegisterSetup(paths);

// The interface speaks the language chosen in the setup wizard from the very first line
Languages.Apply(new UserSettingsStore(paths).Load().UiLanguage);

var app = new CommandApp<ListenCommand>(new TypeRegistrar(services));

app.Configure(config =>
{
    config.SetApplicationName(ApplicationInfo.CommandName);
    config.SetApplicationVersion(ApplicationInfo.Version);

    // The built-in help is English only, so it must not follow the system language
    config.SetApplicationCulture(CultureInfo.InvariantCulture);

    config.AddCommand<ListenCommand>(ListenCommand.Name)
        .WithDescription(ListenCommand.Description);

    config.AddCommand<SetupCommand>(SetupCommand.Name)
        .WithDescription(SetupCommand.Description);
});

int exitCode = await app.RunAsync(args);

// After /restart an Echo that waits above this one starts it anew; without one, this one does it.
// The installed Echo starts through the link to the version in use, which a downloaded update has switched
if (exitCode != Relaunching.ExitCode || Relaunching.HasLauncher(Environment.GetEnvironmentVariable))
    return exitCode;

string program = Environment.ProcessPath!;

if (InstallLayout.Of(AppContext.BaseDirectory) is { } layout)
    program = Path.Combine(layout.Current, Path.GetFileName(program));

return relaunch.Restart(program, Relaunching.Arguments(args, ApplicationInfo.Version));