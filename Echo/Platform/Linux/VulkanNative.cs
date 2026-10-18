using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Echo.Platform.Linux;

/// <summary>
///     The Vulkan loader of the system, asked directly for the graphics cards it can drive.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class VulkanNative
{
    private const string Library = "libvulkan.so.1";

    /// <summary>
    ///     VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO.
    /// </summary>
    public const int InstanceCreateInfoType = 1;

    /// <summary>
    ///     VK_SUCCESS and VK_INCOMPLETE: both mean the list of devices was read.
    /// </summary>
    public const int Success = 0;
    public const int Incomplete = 5;

    /// <summary>
    ///     Room for VkPhysicalDeviceProperties, which is a little over 800 bytes.
    /// </summary>
    public const int PropertiesSize = 1024;

    /// <summary>
    ///     Where VkPhysicalDeviceProperties keeps the kind of the device: after the API and driver versions
    ///     and the vendor and device numbers, four bytes each.
    /// </summary>
    public const int DeviceTypeOffset = 16;

    /// <summary>
    ///     VkInstanceCreateInfo with nothing asked for: no layers, no extensions.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct InstanceCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public uint Flags;
        public IntPtr ApplicationInfo;
        public uint EnabledLayerCount;
        public IntPtr EnabledLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr EnabledExtensionNames;
    }

    [DllImport(Library, EntryPoint = "vkCreateInstance")]
    public static extern int CreateInstance(ref InstanceCreateInfo createInfo, IntPtr allocator, out IntPtr instance);

    [DllImport(Library, EntryPoint = "vkDestroyInstance")]
    public static extern void DestroyInstance(IntPtr instance, IntPtr allocator);

    [DllImport(Library, EntryPoint = "vkEnumeratePhysicalDevices")]
    public static extern int EnumeratePhysicalDevices(IntPtr instance, ref uint count, [Out] IntPtr[]? devices);

    [DllImport(Library, EntryPoint = "vkGetPhysicalDeviceProperties")]
    public static extern void GetPhysicalDeviceProperties(IntPtr device, [Out] byte[] properties);
}