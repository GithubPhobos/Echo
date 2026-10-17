using Echo.Setup.Hardware;
using Whisper.net.LibraryLoader;

namespace Echo.Tests.UnitTests;

public sealed class AccelerationTests
{
    private static readonly RuntimeLibrary[] _runtimeDefaultOrder =
    [
        RuntimeLibrary.Cuda, RuntimeLibrary.Vulkan, RuntimeLibrary.CoreML, RuntimeLibrary.OpenVino, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx,
    ];

    [Fact]
    public void Best_WithoutGraphicsAcceleration_IsProcessor()
    {
        // Arrange
        var hardware = new HardwareInfo(Nvidia: null, IsVulkanAvailable: false, MemoryGb: 8, ProcessorCores: 4);

        // Act & Assert
        Assert.Equal(AccelerationBackend.Cpu, Acceleration.Best(hardware));
    }

    [Theory]
    [InlineData("Cuda", RuntimeLibrary.Cuda)]
    [InlineData("vulkan", RuntimeLibrary.Vulkan)]
    [InlineData("CPU", RuntimeLibrary.Cpu)]
    public void LibraryOrder_PutsChosenBackendFirstAndProcessorLast(string setting, RuntimeLibrary first)
    {
        // Act
        List<RuntimeLibrary> order = Acceleration.LibraryOrder(setting, isCudaInstalled: true, _runtimeDefaultOrder);

        // Assert
        Assert.Equal(first, order[0]);
        Assert.Equal(RuntimeLibrary.Cpu, order[^1]);
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData(null)]
    [InlineData("something else")]
    public void LibraryOrder_AutomaticOrUnknown_KeepsRuntimeOrderWithoutTheBuildEchoDoesNotShip(string? setting)
    {
        // Act & Assert
        Assert.Equal(
            _runtimeDefaultOrder.Where(library => library != RuntimeLibrary.CpuNoAvx),
            Acceleration.LibraryOrder(setting, isCudaInstalled: true, _runtimeDefaultOrder));
        Assert.True(Acceleration.UsesGpu(setting));
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("Cuda")]
    [InlineData("Vulkan")]
    public void LibraryOrder_CudaLibrariesIncomplete_NeverTriesCuda(string setting)
    {
        // Act: with only some of its libraries present, the CUDA runtime would abort the process
        List<RuntimeLibrary> order = Acceleration.LibraryOrder(setting, isCudaInstalled: false, _runtimeDefaultOrder);

        // Assert
        Assert.DoesNotContain(RuntimeLibrary.Cuda, order);
        Assert.Contains(RuntimeLibrary.Cpu, order);
    }

    [Theory]
    [InlineData(nameof(AccelerationBackend.Cuda), RuntimeLibrary.Vulkan, true)]
    [InlineData(nameof(AccelerationBackend.Cuda), RuntimeLibrary.Cuda, false)]
    [InlineData(nameof(AccelerationBackend.Vulkan), RuntimeLibrary.Cpu, true)]
    [InlineData(nameof(AccelerationBackend.Cpu), RuntimeLibrary.Vulkan, false)]
    public void NeedsRestart_OnlyWhenAnotherGraphicsRuntimeIsAlreadyLoaded(string backend,
                                                                                       RuntimeLibrary loaded,
                                                                                       bool expected)
    {
        // Act & Assert
        Assert.Equal(expected, Acceleration.NeedsRestart(Enum.Parse<AccelerationBackend>(backend), loaded));
        Assert.False(Acceleration.NeedsRestart(Enum.Parse<AccelerationBackend>(backend), loaded: null));
    }
}