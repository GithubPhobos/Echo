namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class WindowsFileChooserTests
{
    [Fact]
    public void ParseSelection_OneFile_IsItsFullPath()
    {
        // Act & Assert
        Assert.Equal(
            [@"D:\models\ggml-small.bin"],
            WindowsFileChooser.ParseSelection("D:\\models\\ggml-small.bin"));
    }

    [Fact]
    public void ParseSelection_SeveralFiles_AreJoinedWithTheirFolder()
    {
        // Act: the chooser writes the folder first, then the names, separated by a zero character
        IReadOnlyList<string> files = WindowsFileChooser.ParseSelection("D:\\models\0ggml-small.bin\0ggml-large-v3.bin");

        // Assert
        Assert.Equal([Path.Combine(@"D:\models", "ggml-small.bin"), Path.Combine(@"D:\models", "ggml-large-v3.bin")], files);
    }

    [Fact]
    public void ParseSelection_NothingPicked_IsEmpty()
    {
        // Act & Assert
        Assert.Empty(WindowsFileChooser.ParseSelection(string.Empty));
    }
}