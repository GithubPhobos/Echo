using System.Runtime.Versioning;
using Echo.Platform.Linux;
using Echo.Platform.Windows;
using Echo.Setup.Hardware;
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
    private static readonly object _portalLock = new();

    /// <summary>
    ///     The permission windows of a Linux desktop. One for the whole process: the sessions with the desktop
    ///     live as long as the connection that made them, and every set of services shares it.
    /// </summary>
    private static DesktopPortal? _portal;

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

        if (OperatingSystem.IsLinux())
            return services.AddLinuxPlatform(paths);

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
        services.TryAddSingleton<IDesktopAccess, NoDesktopAccess>();
        services.TryAddSingleton<ISystemRequirements, NoSystemRequirements>();
        services.TryAddSingleton<ISystemLibraries, WindowsSystemLibraries>();

        return services;
    }

    /// <summary>
    ///     Linux: the sound server for recording and sounds; on X11 Echo takes the key and presses keys itself,
    ///     on Wayland it asks the desktop through its permission windows.
    /// </summary>
    [SupportedOSPlatform("linux")]
    private static IServiceCollection AddLinuxPlatform(this IServiceCollection services, IAppPaths paths)
    {
        DisplayServer display = LinuxSession.Detect(Environment.GetEnvironmentVariable);

        // A Wayland desktop runs X11 programs too, through its X11 layer, when it has one
        bool hasX11 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

        var programs = new LinuxPrograms(Environment.GetEnvironmentVariable);
        DesktopPortal portal = SharedPortal(paths);

        // Given as ready objects, so the services do not close the portal along with themselves
        services.TryAddSingleton(programs);
        services.TryAddSingleton(portal);

        services.TryAddSingleton<IUserSettingsStore, UserSettingsStore>();
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<IAudioInput, PulseAudioInput>();
        services.TryAddSingleton<ISoundOutput, PulseSoundOutput>();
        services.TryAddSingleton<ITextClipboard>(_ => new LinuxClipboard(display, hasX11, programs));
        services.TryAddSingleton<IErrorWindow, LogOnlyErrorWindow>();
        services.TryAddSingleton<ICudaLibraries, LinuxCudaLibraries>();
        services.TryAddSingleton<IVulkanSupport, LinuxVulkanSupport>();
        services.TryAddSingleton<ISystemLibraries, LinuxSystemLibraries>();

        services.TryAddSingleton<IFileChooser>(provider => new LinuxFileChooser(
            display == DisplayServer.None ? null : portal, programs, provider.GetRequiredService<IProcessRunner>()));

        services.TryAddSingleton<ISystemRequirements>(provider => new LinuxSystemRequirements(
            display, hasX11, programs, portal, provider.GetRequiredService<IAudioInput>(), provider.GetRequiredService<ISystemLibraries>(), PackageManager.Detect()));

        switch (display)
        {
            case DisplayServer.X11:
                services.TryAddSingleton<IHotkeyListener, X11HotkeyListener>();
                services.TryAddSingleton<IPasteKeys, X11PasteKeys>();
                services.TryAddSingleton<ITerminalFocus>(new X11TerminalFocus(Environment.GetEnvironmentVariable));
                services.TryAddSingleton<IDesktopAccess, NoDesktopAccess>();

                break;

            case DisplayServer.Wayland:
                services.TryAddSingleton<IHotkeyListener, PortalHotkeyListener>();
                services.TryAddSingleton<IPasteKeys, PortalPasteKeys>();
                services.TryAddSingleton<ITerminalFocus, UnknownTerminalFocus>();
                services.TryAddSingleton<IDesktopAccess, PortalDesktopAccess>();

                break;

            default:
                services.TryAddSingleton<IHotkeyListener, NoHotkeyListener>();
                services.TryAddSingleton<IPasteKeys, NoPasteKeys>();
                services.TryAddSingleton<ITerminalFocus, UnknownTerminalFocus>();
                services.TryAddSingleton<IDesktopAccess, NoDesktopAccess>();

                break;
        }

        return services;
    }

    [SupportedOSPlatform("linux")]
    private static DesktopPortal SharedPortal(IAppPaths paths)
    {
        lock (_portalLock)
            return _portal ??= new DesktopPortal(new UserSettingsStore(paths));
    }
}