using System.Globalization;
using Echo.Commands;
using Echo.Extensions;
using Echo.Setup.Steps;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

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

return await app.RunAsync(args);