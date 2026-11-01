namespace Echo.Tests.UnitTests;

public sealed class ModelStepTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    public ModelStepTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void IsModelFile_TellsWhisperModelsFromOtherFiles()
    {
        // Arrange
        string model = Path.Combine(_directory, "ggml-tiny.bin");
        string other = Path.Combine(_directory, "notes.bin");

        File.WriteAllBytes(model, [.. "lmgg"u8, 1, 2, 3]);
        File.WriteAllText(other, "not a model");

        // Act & Assert
        Assert.True(Echo.Setup.Steps.ModelStep.IsModelFile(model));
        Assert.False(Echo.Setup.Steps.ModelStep.IsModelFile(other));
        Assert.False(Echo.Setup.Steps.ModelStep.IsModelFile(Path.Combine(_directory, "missing.bin")));
    }
}