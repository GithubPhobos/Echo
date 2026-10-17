using System.Runtime.InteropServices;

namespace Echo.Platform.Windows;

/// <summary>
///     Echo ships the Vulkan build of Whisper for Windows, so Vulkan is there whenever its driver is.
/// </summary>
internal sealed class WindowsVulkanSupport : IVulkanSupport
{
    private const string VulkanLibrary = "vulkan-1.dll";

    /// <inheritdoc/>
    public bool IsAvailable()
    {
        if (!NativeLibrary.TryLoad(VulkanLibrary, out IntPtr handle))
            return false;

        NativeLibrary.Free(handle);

        return true;
    }
}