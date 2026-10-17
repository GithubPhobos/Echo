using System.Runtime.InteropServices;

namespace Echo.Setup.Hardware;

/// <summary>
///     The requirements of the CUDA libraries this version of the application is built against.
/// </summary>
internal static class CudaRequirements
{
    /// <summary>
    ///     CUDA 13 needs driver branch 580 or newer.
    /// </summary>
    public static readonly Version MinimumDriver = new(580, 0);

    /// <summary>
    ///     CUDA 13 dropped everything older than the Turing generation (RTX 20 and GTX 16 series).
    /// </summary>
    public static readonly Version MinimumComputeCapability = new(7, 5);

    /// <summary>
    ///     The environment variable that makes CUDA count the cards in the same order as the driver tool does.
    ///     Without it CUDA puts the fastest card first, and a card picked by its number could be the wrong one.
    /// </summary>
    public const string DeviceOrderVariable = "CUDA_DEVICE_ORDER";
    public const string DeviceOrderByBus = "PCI_BUS_ID";

    public static CudaSupport Check(HardwareInfo hardware)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return CudaSupport.UnsupportedPlatform;

        if (hardware.Nvidia is null)
            return CudaSupport.NoNvidiaGpu;

        // A driver that cannot report the capability is itself older than the required branch
        if (hardware.Nvidia.ComputeCapability is not null && hardware.Nvidia.ComputeCapability < MinimumComputeCapability)
            return CudaSupport.GpuTooOld;

        if (hardware.Nvidia.DriverVersion < MinimumDriver)
            return CudaSupport.DriverTooOld;

        return CudaSupport.Supported;
    }
}