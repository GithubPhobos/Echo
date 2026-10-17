namespace Echo.Platform.Contracts;

/// <summary>
///     Whether Whisper can run on Vulkan here: the system needs a Vulkan driver, and Echo needs to ship
///     the Vulkan build of Whisper for this system.
/// </summary>
internal interface IVulkanSupport
{
    bool IsAvailable();
}