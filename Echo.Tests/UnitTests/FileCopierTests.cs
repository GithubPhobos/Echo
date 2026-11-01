namespace Echo.Tests.UnitTests;

public sealed class FileCopierTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    public FileCopierTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task CopyAsync_StoppedHalfway_LeavesNothingBehind()
    {
        // Arrange
        string source = Path.Combine(_directory, "source.bin");
        string target = Path.Combine(_directory, "copies", "target.bin");

        File.WriteAllBytes(source, new byte[3 * 1024 * 1024]);

        using var cancellation = new CancellationTokenSource();

        // The copy is stopped as soon as its first part is written
        var progress = new InlineProgress<DownloadProgress>(_ => cancellation.Cancel());

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FileCopier.CopyAsync(source, target, progress, cancellation.Token));

        // Assert: neither a file that looks finished nor a leftover part
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!));
    }

    [Fact]
    public async Task CopyAsync_CopiesTheFileAndKeepsTheOriginal()
    {
        // Arrange
        string source = Path.Combine(_directory, "source.bin");
        string target = Path.Combine(_directory, "copies", "target.bin");

        File.WriteAllBytes(source, [1, 2, 3, 4, 5]);

        // Act
        await FileCopier.CopyAsync(source, target, new InlineProgress<DownloadProgress>(_ => { }), CancellationToken.None);

        // Assert
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(target));
        Assert.Equal(["target.bin"], Directory.GetFiles(Path.GetDirectoryName(target)!).Select(Path.GetFileName));
    }
}