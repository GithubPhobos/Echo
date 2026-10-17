using System.Runtime.Versioning;
using Echo.Platform.Linux;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class LinuxFileChooserTests
{
    [Fact]
    public void Patterns_PlainLibraryNames_AlsoMatchTheirVersionedFiles()
    {
        // Act
        IReadOnlyList<string> patterns = LinuxFileChooser.Patterns("libcudart.so.13;libcublas.so.13");

        // Assert: 'libcublas.so.13' is often a link to 'libcublas.so.13.8.0.4'
        Assert.Equal(["libcudart.so.13*", "libcublas.so.13*"], patterns);
    }

    [Fact]
    public void Patterns_Wildcards_AreKeptAsTheyAre()
    {
        // Act & Assert
        Assert.Equal(["*.bin"], LinuxFileChooser.Patterns("*.bin"));
    }
}