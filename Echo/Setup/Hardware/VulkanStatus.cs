namespace Echo.Setup.Hardware;

/// <summary>
///     Whether Whisper can run on Vulkan, and why not when it cannot.
/// </summary>
internal enum VulkanStatus
{
    /// <summary>
    ///     A graphics card with a Vulkan driver, and the Vulkan build of Whisper for this system.
    /// </summary>
    Available,

    /// <summary>
    ///     No Vulkan driver on the computer.
    /// </summary>
    NoDriver,

    /// <summary>
    ///     Vulkan is there, but it sees no graphics card, only its software stand-in that draws on the processor
    ///     and runs recognition slower than the processor build itself.
    /// </summary>
    SoftwareOnly
}