using Echo.Extensions;
using Microsoft.Extensions.Hosting;

Console.OutputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.SetupConfiguration();
builder.Services.ConfigureSerilog(builder.Configuration);
builder.Services.RegisterServices();

var host = builder.Build();

try
{
    await host.RunAsync();
}
catch (Exception exc)
{
    Log.Fatal(exc, "{ExplosionEmoji} Application terminated unexpectedly",
        LoggerConstants.ExplosionEmoji);
}
finally
{
    Log.CloseAndFlush();
}