using System.Runtime.Versioning;
using Echo.Platform.Windows;
using GregsStack.InputSimulatorStandard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TextCopy;

namespace Echo.Extensions;

/// <summary>
///     The one place where Echo picks the system it runs on and registers that system's way of doing
///     what every system does differently: recording, sounds, the key, the clipboard, pasting and the rest.
/// </summary>
internal static class PlatformServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the implementations of the system Echo runs on. Every place that builds services calls it,
    ///     and calling it again on the same services changes nothing.
    /// </summary>
    /// <param name="services"><see cref="IServiceCollection"/></param>
    /// <param name="paths"><see cref="IAppPaths"/></param>
    internal static IServiceCollection AddPlatform(this IServiceCollection services, IAppPaths paths)
    {
        if (OperatingSystem.IsWindows())
            return services.AddWindowsPlatform();

        throw new PlatformNotSupportedException($"{ApplicationInfo.CommandName} runs on Windows and Linux.");
    }

    /// <summary>
    ///     Windows: NAudio for recording and sounds, a keyboard hook for the key, SendInput for pasting.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.TryAddSingleton<IAudioInput, WaveInAudioInput>();
        services.TryAddSingleton<ISoundOutput, WasapiSoundOutput>();
        services.TryAddSingleton<IHotkeyListener, SharpHookHotkeyListener>();
        services.TryAddSingleton<IClipboard, Clipboard>();
        services.TryAddSingleton<ITextClipboard, WindowsClipboard>();
        services.TryAddSingleton<IInputSimulator, InputSimulator>();
        services.TryAddSingleton<IPasteKeys, CtrlVPasteKeys>();
        services.TryAddSingleton<ITerminalFocus, WindowsTerminalFocus>();
        services.TryAddSingleton<IFileChooser, WindowsFileChooser>();
        services.TryAddSingleton<IErrorWindow, WindowsErrorWindow>();
        services.TryAddSingleton<ICudaLibraries, WindowsCudaLibraries>();
        services.TryAddSingleton<IVulkanSupport, WindowsVulkanSupport>();

        return services;
    }
}