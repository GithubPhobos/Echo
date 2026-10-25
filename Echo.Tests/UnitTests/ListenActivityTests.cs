using Echo.Listening;

namespace Echo.Tests.UnitTests;

public sealed class ListenActivityTests
{
    [Fact]
    public void SetState_ReadyForTheKey_IsStarted()
    {
        // Arrange
        var activity = new ListenActivity(new ScreenLog(), () => false);

        // Act
        activity.SetState(ListenState.Listening);

        // Assert
        Assert.True(activity.Started.IsCompleted);
    }

    [Fact]
    public void SetState_StartUpFailed_IsStarted()
    {
        // Arrange
        var activity = new ListenActivity(new ScreenLog(), () => false);

        // Act
        activity.SetState(ListenState.Failed);

        // Assert
        Assert.True(activity.Started.IsCompleted);
    }

    [Fact]
    public void SetState_PhraseSaidWhileTheModelLoads_IsNotStartedYet()
    {
        // Arrange
        var activity = new ListenActivity(new ScreenLog(), () => false);

        // Act: the key is Echo's before the model is loaded, so a phrase can be recorded meanwhile
        activity.SetState(ListenState.Recording);
        activity.SetState(ListenState.Transcribing);
        activity.SetState(ListenState.Starting);

        // Assert
        Assert.False(activity.Started.IsCompleted);
    }

    [Fact]
    public void SetState_ReadyWhilePaused_IsStarted()
    {
        // Arrange
        var activity = new ListenActivity(new ScreenLog(), () => false) { IsPaused = true };

        // Act
        activity.SetState(ListenState.Listening);

        // Assert
        Assert.Equal(ListenState.Paused, activity.State);
        Assert.True(activity.Started.IsCompleted);
    }
}