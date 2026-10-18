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
    [InlineData(2048, nameof(AccelerationBackend.Cuda), false, false, "small")]
    [InlineData(1024, nameof(AccelerationBackend.Cuda), false, true, "small.en")]
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
    [InlineData(4, 2)]
    [InlineData(8, 4)]
    [InlineData(16, 8)]
    public void Recommend_OnProcessor_SuggestsSmallOnAnyMachine(int memoryGb, int cores)
    {
        // Arrange: Echo is not tuned for weak computers, so a weak one gets small as well
        var hardware = new HardwareInfo(Nvidia: null, IsVulkanAvailable: false, memoryGb, cores);

        // Act
        ModelInfo model = ModelRecommender.Recommend(hardware, AccelerationBackend.Cpu, translate: false, ModelCatalog.For(englishOnly: false));

        // Assert
        Assert.Equal("small", model.Id);
    }

    [Fact]
    public void Rules_NeverSuggestAModelThatIsNotRecommended()
    {
        // Assert
        Assert.All(ModelRecommender.Rules, rule =>
            Assert.All(ModelCatalog.All.Where(model => model.Tier == rule.Tier), model =>
                Assert.False(ModelCatalog.IsNotRecommended(model), model.Id)));
    }
}