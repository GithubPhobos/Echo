namespace Echo.Setup.Hardware;

/// <summary>
///     Why CUDA can or cannot be used on a machine.
/// </summary>
internal enum CudaSupport
{
    Supported,
    NoNvidiaGpu,
    GpuTooOld,
    DriverTooOld,
    UnsupportedPlatform
}