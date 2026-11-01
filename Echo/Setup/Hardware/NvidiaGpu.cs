namespace Echo.Setup.Hardware;

/// <summary>
///     An NVIDIA graphics card as reported by its driver.
/// </summary>
/// <param name="Name">The product name.</param>
/// <param name="MemoryMb">The amount of video memory.</param>
/// <param name="DriverVersion">The installed driver version.</param>
/// <param name="ComputeCapability">The CUDA compute capability, when the driver reports it.</param>
/// <param name="Index">The number of the card, as the driver and the 'GpuDeviceIndex' setting count them.</param>
internal sealed record NvidiaGpu(string Name, int MemoryMb, Version DriverVersion, Version? ComputeCapability, int Index = 0);