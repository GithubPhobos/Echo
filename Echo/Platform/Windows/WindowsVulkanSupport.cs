namespace Echo.Platform.Windows;

/// <summary>
///     Echo ships the Vulkan build of Whisper for Windows, so Vulkan is there whenever its driver is.
/// </summary>
internal sealed class WindowsVulkanSupport : IVulkanSupport
{
    private const string VulkanLibrary = "vulkan-1.dll";

    /// <inheritdoc/>
    public bool IsChosenAutomatically => true;

    /// <inheritdoc/>
    public VulkanStatus Check()
    {
        if (!NativeLibrary.TryLoad(VulkanLibrary, out IntPtr handle))
            return VulkanStatus.NoDriver;

        NativeLibrary.Free(handle);

        return VulkanStatus.Available;
    }
}