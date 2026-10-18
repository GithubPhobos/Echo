using System.Runtime.Versioning;
using Echo.Platform.Linux;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
public sealed class LinuxVulkanSupportTests
{
    [Fact]
    public void IsChosenAutomatically_UntilTriedOnRealCards_IsNo()
    {
        // Act & Assert
        Assert.False(new LinuxVulkanSupport().IsChosenAutomatically);
    }

    [Fact]
    public void IsAvailable_WithOrWithoutAVulkanDriver_AnswersWithoutThrowing()
    {
        if (!OperatingSystem.IsLinux())
            return;

        // Act & Assert: a machine without the Vulkan loader, or with only the processor's software device, says no
        Assert.Null(Record.Exception(() => new LinuxVulkanSupport().IsAvailable()));
    }
}