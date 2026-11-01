namespace Echo.Tests.UnitTests;

public sealed class HardwareProbeTests
{
    [Fact]
    public void ParseNvidiaGpus_OutputOfModernDriver_ReadsAllFields()
    {
        // Act
        NvidiaGpu gpu = Assert.Single(HardwareProbe.ParseNvidiaGpus("0, NVIDIA GeForce RTX 5070 Ti, 16303, 617.14, 12.0\r\n", hasCapability: true));

        // Assert
        Assert.Equal(0, gpu.Index);
        Assert.Equal("NVIDIA GeForce RTX 5070 Ti", gpu.Name);
        Assert.Equal(16303, gpu.MemoryMb);
        Assert.Equal(new Version(617, 14), gpu.DriverVersion);
        Assert.Equal(new Version(12, 0), gpu.ComputeCapability);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NVIDIA-SMI has failed because it couldn't communicate with the NVIDIA driver")]
    public void ParseNvidiaGpus_NoUsableOutput_FindsNoCards(string? output)
    {
        // Act & Assert
        Assert.Empty(HardwareProbe.ParseNvidiaGpus(output, hasCapability: true));
    }

    [Fact]
    public void ParseNvidiaGpus_OldDriverWithoutComputeCapability_LeavesItUnknown()
    {
        // Act
        NvidiaGpu gpu = Assert.Single(HardwareProbe.ParseNvidiaGpus("0, GeForce GTX 1060 6GB, 6144, 472.12", hasCapability: false));

        // Assert
        Assert.Equal("GeForce GTX 1060 6GB", gpu.Name);
        Assert.Null(gpu.ComputeCapability);
    }

    [Fact]
    public void ParseNvidiaGpus_SeveralCards_KeepsTheNumberOfEachCard()
    {
        // Act
        IReadOnlyList<NvidiaGpu> cards = HardwareProbe.ParseNvidiaGpus(
            "0, NVIDIA GeForce RTX 4060, 8188, 617.14, 8.9\n1, NVIDIA RTX A6000, 49140, 617.14, 8.6\n",
            hasCapability: true);

        // Assert
        Assert.Equal([0, 1], cards.Select(card => card.Index));
        Assert.Equal(["NVIDIA GeForce RTX 4060", "NVIDIA RTX A6000"], cards.Select(card => card.Name));
        Assert.Equal(49140, cards[1].MemoryMb);
    }
}