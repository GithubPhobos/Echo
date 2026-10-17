using Echo.Commands;
using Echo.Listening;
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
}