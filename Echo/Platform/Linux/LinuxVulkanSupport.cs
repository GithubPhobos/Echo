namespace Echo.Platform.Linux;

/// <summary>
///     Echo does not ship the Vulkan build of Whisper for Linux, so Vulkan is not offered whatever the driver.
/// </summary>
internal sealed class LinuxVulkanSupport : IVulkanSupport
{
    /// <inheritdoc/>
    public bool IsAvailable() => false;
}