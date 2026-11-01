namespace Echo.Setup.Hardware;

/// <summary>
///     What a Whisper model can run on.
/// </summary>
internal enum AccelerationBackend
{
    Cuda,
    Vulkan,
    Cpu
}