namespace Echo.Tests.UnitTests;

public sealed class InstallLockTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));
    private readonly InstallLayout _layout;

    public InstallLockTests()
    {
        Directory.CreateDirectory(_root);
        _layout = new InstallLayout(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void TryTake_HeldByAnother_IsNotTaken()
    {
        // Arrange: another Echo holds it, through a file of its own
        using InstallLock? other = InstallLock.TryTake(_layout);

        // Act
        using InstallLock? taken = InstallLock.TryTake(_layout);

        // Assert
        Assert.NotNull(other);
        Assert.Null(taken);
    }

    [Fact]
    public void TryTake_ReleasedByAnother_IsTaken()
    {
        // Arrange
        InstallLock.TryTake(_layout)!.Dispose();

        // Act
        using InstallLock? taken = InstallLock.TryTake(_layout);

        // Assert
        Assert.NotNull(taken);
    }

    [Fact]
    public async Task TakeAsync_HeldByAnother_WaitsUntilItIsReleased()
    {
        // Arrange
        using InstallLock other = InstallLock.TryTake(_layout)!;

        // Act
        Task<InstallLock> taking = InstallLock.TakeAsync(_layout);
        await Task.Delay(500);

        bool isTakenWhileHeld = taking.IsCompleted;

        other.Dispose();
        using InstallLock taken = await taking.WaitAsync(TimeSpan.FromSeconds(10));

        // Assert
        Assert.False(isTakenWhileHeld);
    }

    [Fact]
    public void TryTake_NoFolderOfTheInstallation_Throws()
    {
        // Arrange: an error that waiting would not mend
        var layout = new InstallLayout(Path.Combine(_root, "missing"));

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => InstallLock.TryTake(layout));
    }

    [LinuxFact("flock")]
    public async Task TryTake_HeldByTheInstallScript_IsNotTaken()
    {
        // Arrange: the install script locks the file with the flock program; it is waited for until it holds it.
        // The program it runs does not hold the lock as well, so the lock goes with the flock program
        using Process script = Process.Start("flock", ["--close", _layout.LockFile, "sleep", "30"]);

        await WaitUntilAsync(() =>
        {
            using InstallLock? probe = InstallLock.TryTake(_layout);

            return probe is null;
        });

        // Act
        using InstallLock? taken = InstallLock.TryTake(_layout);

        script.Kill(entireProcessTree: true);
        await script.WaitForExitAsync();

        using InstallLock? afterwards = InstallLock.TryTake(_layout);

        // Assert: and once the script is done, it is taken
        Assert.Null(taken);
        Assert.NotNull(afterwards);
    }

    [LinuxFact("flock")]
    public async Task TryTake_InstallScriptAfterwards_CannotTakeIt()
    {
        // Arrange
        using InstallLock? taken = InstallLock.TryTake(_layout);

        // Act: as the install script asks, without waiting
        using Process script = Process.Start("flock", ["-n", _layout.LockFile, "true"]);
        await script.WaitForExitAsync();

        // Assert
        Assert.NotNull(taken);
        Assert.Equal(1, script.ExitCode);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(10), "The condition did not come true.");
            await Task.Delay(20);
        }
    }
}