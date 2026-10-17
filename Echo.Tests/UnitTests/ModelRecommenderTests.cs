using Echo.Setup.Hardware;
using Echo.Setup.Models;

namespace Echo.Tests.UnitTests;

public sealed class ModelRecommenderTests
{
    [Theory]
    [InlineData(16303, nameof(AccelerationBackend.Cuda), false, false, "large-v3-turbo")]
    [InlineData(16303, nameof(AccelerationBackend.Cuda), true, false, "medium")]
    [InlineData(6144, nameof(AccelerationBackend.Vulkan), false, false, "medium")]
    [InlineData(4096, nameof(AccelerationBackend.Cuda), false, true, "small.en")]
    [InlineData(2048, nameof(AccelerationBackend.Cuda), false, false, "base")]
    [InlineData(16303, nameof(AccelerationBackend.Cpu), false, false, "small")]
    public void Recommend_FollowsTheTable(int videoMemoryMb,
                                                          string backend,
                                                          bool translate,
                                                          bool englishOnly,
                                                          string expected)
    {
        // Arrange
        var hardware = new HardwareInfo(
            new NvidiaGpu("GPU", videoMemoryMb, new Version(600, 0), new Version(8, 9)),
            IsVulkanAvailable: true,
            MemoryGb: 32,
            ProcessorCores: 16);

        // Act
        ModelInfo model = ModelRecommender.Recommend(
            hardware, Enum.Parse<AccelerationBackend>(backend), translate, ModelCatalog.For(englishOnly));

        // Assert
        Assert.Equal(expected, model.Id);
    }

    [Theory]
    [InlineData(4, 2, "tiny")]
    [InlineData(8, 4, "base")]
    [InlineData(16, 8, "small")]
    public void Recommend_OnProcessor_DependsOnMemoryAndCores(int memoryGb, int cores, string expected)
    {
        // Arrange
        var hardware = new HardwareInfo(Nvidia: null, IsVulkanAvailable: false, memoryGb, cores);

        // Act
        ModelInfo model = ModelRecommender.Recommend(hardware, AccelerationBackend.Cpu, translate: false, ModelCatalog.For(englishOnly: false));

        // Assert
        Assert.Equal(expected, model.Id);
    }
}