namespace Echo.Setup.Hardware;

/// <summary>
///     What the setup wizard needs to know about the machine.
/// </summary>
/// <param name="Nvidia">The NVIDIA graphics card the model would run on, if there is any.</param>
/// <param name="IsVulkanAvailable">Whether Whisper can run on Vulkan: a driver is installed and Echo ships Vulkan for this system.</param>
/// <param name="MemoryGb">The amount of system memory.</param>
/// <param name="ProcessorCores">The number of logical processor cores.</param>
internal sealed record HardwareInfo(NvidiaGpu? Nvidia, bool IsVulkanAvailable, int MemoryGb, int ProcessorCores)
{
    /// <summary>
    ///     Every NVIDIA graphics card in the machine. With more than one, the user picks which to use.
    /// </summary>
    public IReadOnlyList<NvidiaGpu> NvidiaCards { get; init; } = Nvidia is null ? [] : [Nvidia];
}