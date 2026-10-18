using Whisper.net.LibraryLoader;

namespace Echo.Setup.Hardware;

/// <summary>
///     Turns the 'HardwareBackend' setting into what the Whisper runtime should load.
/// </summary>
internal static class Acceleration
{
    public const string Auto = "Auto";

    /// <summary>
    ///     The values accepted by the 'HardwareBackend' setting besides <see cref="Auto"/>.
    /// </summary>
    public static string ToSetting(AccelerationBackend backend) => backend.ToString();

    /// <summary>
    ///     Parses the setting; anything unknown, including <see cref="Auto"/>, gives null.
    /// </summary>
    public static AccelerationBackend? Parse(string? setting) =>
        Enum.TryParse(setting, ignoreCase: true, out AccelerationBackend backend) && Enum.IsDefined(backend)
            ? backend
            : null;

    /// <summary>
    ///     The best backend the machine can run.
    /// </summary>
    public static AccelerationBackend Best(HardwareInfo hardware)
    {
        if (CudaRequirements.Check(hardware) == CudaSupport.Supported)
            return AccelerationBackend.Cuda;

        return hardware.IsVulkanAvailable && hardware.IsVulkanChosenAutomatically
            ? AccelerationBackend.Vulkan
            : AccelerationBackend.Cpu;
    }

    /// <summary>
    ///     The order in which the Whisper runtime should try its native libraries. The processor is always
    ///     the last resort, so a broken graphics driver slows dictation down instead of breaking it.
    /// </summary>
    /// <param name="setting">The 'HardwareBackend' setting.</param>
    /// <param name="isCudaInstalled">Whether every NVIDIA library the CUDA runtime needs is in place.</param>
    /// <param name="isVulkanChosenAutomatically">Whether the automatic setting may load Vulkan on this system.</param>
    /// <param name="defaultOrder">The runtime's own order, used for the automatic setting.</param>
    public static List<RuntimeLibrary> LibraryOrder(string? setting,
                                                    bool isCudaInstalled,
                                                    bool isVulkanChosenAutomatically,
                                                    IReadOnlyList<RuntimeLibrary> defaultOrder)
    {
        List<RuntimeLibrary> order = Parse(setting) switch
        {
            AccelerationBackend.Cuda => [RuntimeLibrary.Cuda, RuntimeLibrary.Cpu],
            AccelerationBackend.Vulkan => [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu],
            AccelerationBackend.Cpu => [RuntimeLibrary.Cpu],
            _ => [.. defaultOrder.Where(library => library != RuntimeLibrary.Vulkan || isVulkanChosenAutomatically)]
        };

        // With some of its libraries missing the CUDA runtime does not fail, it takes the process down
        if (!isCudaInstalled)
            order.Remove(RuntimeLibrary.Cuda);

        // Echo does not ship the build for processors without AVX: a processor that needs it is turned away
        // before setup goes any further
        order.Remove(RuntimeLibrary.CpuNoAvx);

        return order;
    }

    /// <summary>
    ///     Whether the runtime already loaded in this process is not the one <paramref name="backend"/> needs.
    ///     The runtime is loaded once per process, so only a restart can change it.
    /// </summary>
    public static bool NeedsRestart(AccelerationBackend backend, RuntimeLibrary? loaded) => backend switch
    {
        AccelerationBackend.Cuda => loaded is not null and not RuntimeLibrary.Cuda,
        AccelerationBackend.Vulkan => loaded is not null and not RuntimeLibrary.Vulkan,

        // Any runtime can be told to stay on the processor
        _ => false
    };

    public static bool UsesGpu(string? setting) =>
        Parse(setting) != AccelerationBackend.Cpu;
}