namespace Echo.Platform.Contracts;

/// <summary>
///     Whether Whisper can run on Vulkan here: the system needs a Vulkan driver, and Echo needs to ship
///     the Vulkan build of Whisper for this system.
/// </summary>
internal interface IVulkanSupport
{
    /// <summary>
    ///     Whether Vulkan can be used, and why not when it cannot.
    /// </summary>
    VulkanStatus Check();

    /// <summary>
    ///     Whether "automatic" may pick Vulkan. Where it has not yet been tried on real graphics cards,
    ///     Vulkan is offered only for the user to pick by hand, and "automatic" stays on the processor.
    /// </summary>
    bool IsChosenAutomatically { get; }
}