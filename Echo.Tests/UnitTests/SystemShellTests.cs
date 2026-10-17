namespace Echo.Tests.UnitTests;

public sealed class SystemShellTests
{
    [Fact]
    public void FreeSpaceOf_FolderOnADiskMountedInsideTheTree_CountsThatDisk()
    {
        if (OperatingSystem.IsWindows())
            return;

        // Arrange: a full root and a roomy home, as on a Linux machine with a separate '/home'
        var freeSpace = new Dictionary<string, long> { ["/"] = 10, ["/home"] = 5_000 };

        // Act
        long free = SystemShell.FreeSpaceOf("/home/user/.local/share/echooo/models", freeSpace.Keys, root => freeSpace[root]);

        // Assert
        Assert.Equal(5_000, free);
    }

    [Fact]
    public void FreeSpaceOf_FolderBesideTheMountedDisk_CountsTheRoot()
    {
        if (OperatingSystem.IsWindows())
            return;

        // Arrange: '/homework' only starts with the same letters as '/home'
        var freeSpace = new Dictionary<string, long> { ["/"] = 10, ["/home"] = 5_000 };

        // Act
        long free = SystemShell.FreeSpaceOf("/homework/models", freeSpace.Keys, root => freeSpace[root]);

        // Assert
        Assert.Equal(10, free);
    }

    [Fact]
    public void FreeSpaceOf_DriveLetter_CountsThatDrive()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Arrange
        var freeSpace = new Dictionary<string, long> { [@"C:\"] = 10, [@"D:\"] = 5_000 };

        // Act
        long free = SystemShell.FreeSpaceOf(@"d:\Echo\models", freeSpace.Keys, root => freeSpace[root]);

        // Assert: Windows does not tell capital letters from small ones
        Assert.Equal(5_000, free);
    }
}