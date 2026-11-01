namespace Echo.Platform.Linux;

/// <summary>
///     The Vulkan build of Whisper for Linux, for graphics cards of AMD and Intel. It has not yet been tried on
///     real cards, so it is offered only for the user to pick by hand; "automatic" never picks it.
/// </summary>
/// <remarks>
///     The Vulkan library alone proves nothing: it is installed with the desktop on machines without a graphics
///     card, and even then it lists a device that draws on the processor (llvmpipe), slower for recognition
///     than the processor build itself. So Vulkan counts as available only with a real graphics card.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class LinuxVulkanSupport : IVulkanSupport
{
    /// <summary>
    ///     VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU, DISCRETE_GPU and VIRTUAL_GPU: a card, not the processor.
    /// </summary>
    private static readonly int[] _graphicsCards = [1, 2, 3];

    /// <inheritdoc/>
    public bool IsChosenAutomatically => false;

    /// <inheritdoc/>
    public VulkanStatus Check()
    {
        try
        {
            return Classify(DeviceTypes());
        }
        catch (Exception exc) when (exc is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            // No Vulkan loader on the system
            return VulkanStatus.NoDriver;
        }
    }

    /// <summary>
    ///     What the kinds of the devices Vulkan offers mean for Echo.
    /// </summary>
    internal static VulkanStatus Classify(IReadOnlyList<int> deviceTypes)
    {
        if (deviceTypes.Any(_graphicsCards.Contains))
            return VulkanStatus.Available;

        return deviceTypes.Count > 0 ? VulkanStatus.SoftwareOnly : VulkanStatus.NoDriver;
    }

    /// <summary>
    ///     The kinds of the devices the Vulkan drivers of the system offer.
    /// </summary>
    private static List<int> DeviceTypes()
    {
        var createInfo = new VulkanNative.InstanceCreateInfo { Type = VulkanNative.InstanceCreateInfoType };

        if (VulkanNative.CreateInstance(ref createInfo, IntPtr.Zero, out IntPtr instance) != VulkanNative.Success)
            return [];

        try
        {
            uint count = 0;

            if (VulkanNative.EnumeratePhysicalDevices(instance, ref count, null) != VulkanNative.Success || count == 0)
                return [];

            var devices = new IntPtr[count];
            int result = VulkanNative.EnumeratePhysicalDevices(instance, ref count, devices);

            if (result != VulkanNative.Success && result != VulkanNative.Incomplete)
                return [];

            var types = new List<int>();

            foreach (IntPtr device in devices.Take((int)count))
            {
                byte[] properties = new byte[VulkanNative.PropertiesSize];

                VulkanNative.GetPhysicalDeviceProperties(device, properties);
                types.Add(BitConverter.ToInt32(properties, VulkanNative.DeviceTypeOffset));
            }

            return types;
        }
        finally
        {
            VulkanNative.DestroyInstance(instance, IntPtr.Zero);
        }
    }
}