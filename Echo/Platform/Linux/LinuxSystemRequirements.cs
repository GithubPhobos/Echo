using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Echo.Platform.Linux;

/// <summary>
///     What Echo needs on Linux, which depends on the distribution and the desktop: the sound server, a program
///     for the clipboard, the permission windows of a Wayland desktop and, for the NVIDIA libraries, xz.
/// </summary>
/// <param name="display">The display server of the session.</param>
/// <param name="hasX11">Whether X11 programs can run in the session.</param>
/// <param name="programs">Finds the programs Echo needs.</param>
/// <param name="portal">The permission windows of the desktop.</param>
/// <param name="audioInput">The microphones, as the sound server lists them.</param>
/// <param name="systemLibraries">The libraries of the system recognition needs.</param>
/// <param name="packageManager">How this distribution installs programs; null for one Echo does not know.</param>
[SupportedOSPlatform("linux")]
internal sealed class LinuxSystemRequirements(DisplayServer display,
                                              bool hasX11,
                                              LinuxPrograms programs,
                                              DesktopPortal portal,
                                              IAudioInput audioInput,
                                              ISystemLibraries systemLibraries,
                                              PackageManager? packageManager) : ISystemRequirements
{
    private const string SoundLibrary = "libpulse-simple.so.0";

    private static readonly Dictionary<string, string> _soundPackages = new()
    {
        [PackageManager.Debian] = "libpulse0",
        [PackageManager.Fedora] = "pulseaudio-libs",
        [PackageManager.Arch] = "libpulse",
        [PackageManager.Suse] = "libpulse0",
    };

    private static readonly Dictionary<string, string> _openMpPackages = new()
    {
        [PackageManager.Debian] = "libgomp1",
        [PackageManager.Fedora] = "libgomp",
        [PackageManager.Arch] = "gcc-libs",
        [PackageManager.Suse] = "libgomp1",
    };

    private static readonly Dictionary<string, string> _xzPackages = new()
    {
        [PackageManager.Debian] = "xz-utils",
        [PackageManager.Fedora] = "xz",
        [PackageManager.Arch] = "xz",
        [PackageManager.Suse] = "xz",
    };

    private readonly DisplayServer _display = display;
    private readonly bool _hasX11 = hasX11;
    private readonly LinuxPrograms _programs = programs;
    private readonly DesktopPortal _portal = portal;
    private readonly IAudioInput _audioInput = audioInput;
    private readonly ISystemLibraries _systemLibraries = systemLibraries;
    private readonly PackageManager? _packageManager = packageManager;

    /// <inheritdoc/>
    public IReadOnlyList<SystemRequirement> Check()
    {
        var requirements = new List<SystemRequirement>
        {
            new(Strings.RequireDesktop, _display != DisplayServer.None, Strings.RequireDesktopWithout, Fix: null),
            new(Strings.RequireRecognition, _systemLibraries.FindMissing().Count == 0, Strings.RequireRecognitionWithout, Install(_openMpPackages)),
        };

        bool hasSound = HasLibrary(SoundLibrary);

        requirements.Add(new(Strings.RequireSound, hasSound, Strings.RequireSoundWithout, Install(_soundPackages)));

        if (hasSound)
            requirements.Add(new(Strings.RequireMicrophone, _audioInput.GetDevices().Count > 0, Strings.RequireMicrophoneWithout, Strings.RequireMicrophoneFix));

        requirements.Add(Clipboard());

        if (_display == DisplayServer.Wayland)
        {
            requirements.Add(new(Strings.RequireShortcuts, HasPortal(DesktopPortal.ShortcutsInterface), Strings.RequireShortcutsWithout, Fix: null));
            requirements.Add(new(Strings.RequirePaste, HasPortal(DesktopPortal.RemoteDesktopInterface), Strings.RequirePasteWithout, Fix: null));
        }

        if (_display != DisplayServer.None)
        {
            bool hasChooser = HasPortal(DesktopPortal.FileChooserInterface) || _programs.Exists("zenity") || _programs.Exists("kdialog");

            requirements.Add(new(Strings.RequireFileChooser, hasChooser, Strings.RequireFileChooserWithout, Install(Same("zenity"))));
        }

        // Only a machine with an NVIDIA card downloads the NVIDIA libraries
        if (_programs.Exists("nvidia-smi"))
            requirements.Add(new(Strings.RequireUnpacker, _programs.Exists("tar") && _programs.Exists("xz"), Strings.RequireUnpackerWithout, Install(_xzPackages)));

        return requirements;
    }

    /// <summary>
    ///     xsel where X11 programs run, wl-copy on a Wayland desktop without them.
    /// </summary>
    private SystemRequirement Clipboard()
    {
        IReadOnlyList<(string Program, string Clipboard, string Selection)> writers = LinuxClipboard.Writers(_display, _hasX11);

        bool isMet = writers.Any(writer => _programs.Exists(writer.Program));
        string suggested = _hasX11 ? "xsel" : "wl-clipboard";

        return new(Strings.Format(nameof(Strings.RequireClipboard), suggested), isMet, Strings.RequireClipboardWithout, Install(Same(suggested)));
    }

    private bool HasPortal(string portalInterface) =>
        _portal.VersionAsync(portalInterface).GetAwaiter().GetResult() is not null;

    private string Install(IReadOnlyDictionary<string, string> packages) =>
        _packageManager is null
            ? Strings.Format(nameof(Strings.RequireInstallPackage), packages.TryGetValue(PackageManager.Debian, out string? name) ? name : packages.Values.First())
            : Strings.Format(nameof(Strings.RequireInstall), _packageManager.Install(packages));

    private static Dictionary<string, string> Same(string package) =>
        new() { [PackageManager.Debian] = package };

    private static bool HasLibrary(string library)
    {
        if (!NativeLibrary.TryLoad(library, out IntPtr handle))
            return false;

        NativeLibrary.Free(handle);

        return true;
    }
}