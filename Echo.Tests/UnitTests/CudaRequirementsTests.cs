using Echo.Setup.Hardware;

namespace Echo.Tests.UnitTests;

public sealed class CudaRequirementsTests
{
    [Theory]
    [InlineData("12.0", "617.14", nameof(CudaSupport.Supported))]
    [InlineData("7.5", "580.88", nameof(CudaSupport.Supported))]
    [InlineData("6.1", "581.15", nameof(CudaSupport.GpuTooOld))]
    [InlineData("8.9", "572.83", nameof(CudaSupport.DriverTooOld))]
    public void Check_FollowsCuda13Requirements(string capability, string driver, string expected)
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Arrange
        var hardware = new HardwareInfo(
            new NvidiaGpu("GPU", 8192, Version.Parse(driver), Version.Parse(capability)),
            IsVulkanAvailable: true,
            MemoryGb: 16,
            ProcessorCores: 8);

        // Act & Assert
        Assert.Equal(expected, CudaRequirements.Check(hardware).ToString());
    }
}