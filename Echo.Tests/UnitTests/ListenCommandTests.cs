using Echo.Commands;
using Microsoft.Extensions.Hosting;

namespace Echo.Tests.UnitTests;

public sealed class ListenCommandTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SummarizeSettings_HistoryIsOnOrOff_AlwaysShowsWhereItsFilesAre(bool enabled)
    {
        // Arrange: existing history files remain useful after recording more phrases is disabled
        string directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(directory, directory);
        var command = new ListenCommand(new TestConsole(), paths, null!, Mock.Of<IAudioInput>(), Mock.Of<ITerminalFocus>());
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Memory:Enabled"] = enabled.ToString(),
        }).Build();

        try
        {
            // Act
            List<(string Label, string Value)> rows = command.SummarizeSettings(
                configuration, new ListenActivity(new ScreenLog(), () => false), new LoggingLevelSwitch());

            // Assert
            string value = Assert.Single(rows, row => row.Label == Strings.AdditionalMemory).Value;
            Assert.Contains(paths.MemoryDirectory, value);

            if (!enabled)
                Assert.Contains(Strings.ValueOff, value);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_ServicesThatCannotBeMade_AreShownAsAFailedStart()
    {
        // Arrange: a setting of the wrong kind stops the services from being made at all
        var host = new Mock<IHost>();
        host.Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Failed to convert configuration value 'bad' at 'WhisperSettings:Threads'."));

        var activity = new ListenActivity(new ScreenLog(), () => false);

        // Act
        await ListenCommand.StartAsync(host.Object, activity, CancellationToken.None);

        // Assert: the screen says so instead of staying at "starting"
        Assert.Equal(ListenState.Failed, activity.State);
        Assert.Contains(activity.Log.Snapshot(), entry => entry.Kind == EntryKind.Error && entry.Text.Contains("'bad'"));
    }

    [Fact]
    public void ScreenLifetime_SystemAsksEchoToStop_EndsTheScreen()
    {
        // Arrange: the host the screen runs with, made the same way
        using IHost host = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory }).Build();
        using CancellationTokenSource running = ListenCommand.ScreenLifetime(host, CancellationToken.None);

        // Act: a stop signal reaches the host as a request to stop, which is all the host does with it
        host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        // Assert
        Assert.True(running.IsCancellationRequested);
    }

    [Fact]
    public void ScreenLifetime_NothingAsksEchoToStop_KeepsTheScreen()
    {
        // Arrange
        using IHost host = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory }).Build();

        // Act
        using CancellationTokenSource running = ListenCommand.ScreenLifetime(host, CancellationToken.None);

        // Assert
        Assert.False(running.IsCancellationRequested);
    }
}