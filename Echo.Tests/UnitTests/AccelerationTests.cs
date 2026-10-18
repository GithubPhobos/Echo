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
        List<RuntimeLibrary> order = Acceleration.LibraryOrder(setting, isCudaInstalled: true, isVulkanChosenAutomatically: true, _runtimeDefaultOrder);

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
            Acceleration.LibraryOrder(setting, isCudaInstalled: true, isVulkanChosenAutomatically: true, _runtimeDefaultOrder));
        Assert.True(Acceleration.UsesGpu(setting));
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("Cuda")]
    [InlineData("Vulkan")]
    public void LibraryOrder_CudaLibrariesIncomplete_NeverTriesCuda(string setting)
    {
        // Act: with only some of its libraries present, the CUDA runtime would abort the process
        List<RuntimeLibrary> order = Acceleration.LibraryOrder(setting, isCudaInstalled: false, isVulkanChosenAutomatically: true, _runtimeDefaultOrder);

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

    [Fact]
    public void Best_VulkanOfferedOnlyByHand_IsProcessor()
    {
        // Arrange: Linux, where Vulkan has not been tried on real cards yet
        var hardware = new HardwareInfo(Nvidia: null, IsVulkanAvailable: true, MemoryGb: 8, ProcessorCores: 4)
        {
            IsVulkanChosenAutomatically = false
        };

        // Act & Assert
        Assert.Equal(AccelerationBackend.Cpu, Acceleration.Best(hardware));
    }

    [Fact]
    public void LibraryOrder_VulkanOfferedOnlyByHand_AutomaticNeverLoadsItButAChoiceByHandDoes()
    {
        // Act
        List<RuntimeLibrary> automatic = Acceleration.LibraryOrder("Auto", isCudaInstalled: true, isVulkanChosenAutomatically: false, _runtimeDefaultOrder);
        List<RuntimeLibrary> byHand = Acceleration.LibraryOrder("Vulkan", isCudaInstalled: true, isVulkanChosenAutomatically: false, _runtimeDefaultOrder);

        // Assert: picked by hand it is tried first, with the processor behind it
        Assert.DoesNotContain(RuntimeLibrary.Vulkan, automatic);
        Assert.Equal([RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu], byHand);
    }
}