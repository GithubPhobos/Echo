using Echo.Commands;
using Echo.Listening;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Echo.Tests.UnitTests;

public sealed class ListenCommandTests
{
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