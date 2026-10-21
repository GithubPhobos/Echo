using System.Runtime.Versioning;
using Echo.Platform.Linux;
using Echo.Setup.Hardware;

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

    [LinuxFact]
    public void IsAvailable_WithOrWithoutAVulkanDriver_AnswersWithoutThrowing()
    {
        // Act & Assert: a machine without the Vulkan loader says so instead of failing
        Assert.Null(Record.Exception(() => new LinuxVulkanSupport().Check()));
    }

    [Theory]
    [InlineData(new int[0], nameof(VulkanStatus.NoDriver))]
    [InlineData(new[] { 4 }, nameof(VulkanStatus.SoftwareOnly))]
    [InlineData(new[] { 4, 1 }, nameof(VulkanStatus.Available))]
    [InlineData(new[] { 2 }, nameof(VulkanStatus.Available))]
    public void Classify_KindsOfDevices_OnlyARealGraphicsCardCounts(int[] deviceTypes, string expected)
    {
        // Act & Assert: 4 is the software device that draws on the processor (llvmpipe), 1 a built-in card, 2 a separate one
        Assert.Equal(expected, LinuxVulkanSupport.Classify(deviceTypes).ToString());
    }
}