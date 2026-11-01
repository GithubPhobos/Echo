using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Echo.Setup;
using Echo.Setup.Audio;
using Echo.Setup.Check;
using Echo.Setup.Downloads;
using Echo.Setup.Hardware;
using Echo.Setup.Hotkeys;
using Echo.Setup.Steps;
using Echo.UI;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using Whisper.net.LibraryLoader;

namespace Echo.Tests.UnitTests;

/// <summary>
///     Walks through the whole setup wizard with a scripted keyboard and stand-ins for the hardware.
/// </summary>
[Collection(CultureCollection.Name)]
public sealed partial class SetupWizardTests : IDisposable
{
    private static readonly NvidiaGpu _modernGpu = new("NVIDIA GeForce RTX 5070 Ti", 16303, new Version(617, 14), new Version(12, 0));

    private static readonly string[] _libraries = ["cudart64_13.dll", "cublas64_13.dll", "cublasLt64_13.dll"];

    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;
    private readonly string _home = Path.Combine(Path.GetTempPath(), "echooo-tests", Guid.NewGuid().ToString("N"));

    private readonly ScriptedInput _keys = new();
    private readonly TestConsole _console = new TestConsole().Width(120).Height(40).Interactive();
    private readonly FakeHotkeyCaptureFactory _hotkeys = new();
    private readonly FakeMicrophoneProbe _microphones = new();
    private readonly FakeDictationTester _dictation = new();
    private readonly Mock<ICudaRuntime> _cuda = new();
    private readonly Mock<IFileDownloader> _downloader = new();
    private readonly Mock<ISystemShell> _shell = new();
    private readonly Mock<ISystemRequirements> _requirements = new();
    private readonly Mock<IDesktopAccess> _desktop = new();

    private HardwareInfo _hardware = new(Nvidia: null, IsVulkanAvailable: false, MemoryGb: 16, ProcessorCores: 8);
    private RuntimeLibrary? _loadedRuntime;

    public SetupWizardTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");

        _keys.Screen = () => _console.Output;

        _shell.Setup(shell => shell.GetFreeSpace(It.IsAny<string>())).Returns(long.MaxValue);
        _shell.Setup(shell => shell.CreateDirectory(It.IsAny<string>())).Callback((string path) => Directory.CreateDirectory(path));
        _cuda.SetupGet(cuda => cuda.Directory).Returns(Path.Combine(_home, "cuda"));
        _cuda.SetupGet(cuda => cuda.DownloadSizeBytes).Returns(425_000_000);
        _cuda.SetupGet(cuda => cuda.Libraries).Returns(_libraries);
        _cuda.Setup(cuda => cuda.FindMissingLibraries()).Returns([]);
        _requirements.Setup(requirements => requirements.Check()).Returns([]);
        _desktop.SetupGet(desktop => desktop.MayPaste).Returns(true);
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalCulture;
        CultureInfo.DefaultThreadCurrentUICulture = null;

        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);
    }

    [Fact]
    public async Task RunAsync_AnotherInterfaceLanguageChosen_TheNextPagesSpeakIt()
    {
        // Arrange: the interface starts in English, the way it does with English saved
        _keys.Press(ConsoleKey.DownArrow)                                           // interface language: Russian
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)                                                // dictation language: Russian
            .Press(ConsoleKey.Enter)                                                // hardware: automatic
            .Press(ConsoleKey.Enter)                                                // model: the recommended one
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)            // key: keep the default
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)        // test dictation: finish
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert: the pages after the choice are in Russian, not only the saved choice
        Assert.Equal("ru", CreateStore().Load().UiLanguage);
        Assert.Contains("Аппаратное ускорение", _console.Output);
    }

    [Fact]
    public async Task RunAsync_DefaultsOnMachineWithoutGraphicsCard_SavesProcessorSetup()
    {
        // Arrange: Enter on every screen accepts what the wizard suggests
        _keys.Press(ConsoleKey.Enter)                                               // interface language: English
            .Press(ConsoleKey.Enter)                                                // dictation language: English
            .Press(ConsoleKey.Enter)                                                // hardware: automatic
            .Press(ConsoleKey.Enter)                                                // model: the recommended one
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)            // key: keep the default
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)        // test dictation: finish
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        SetupOutcome outcome = await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        UserSettings saved = CreateStore().Load();

        Assert.Equal(SetupOutcome.Completed, outcome);
        Assert.Equal(0, _keys.Remaining);
        Assert.Equal(SetupRunner.CurrentVersion, saved.SetupVersion);
        Assert.Equal("en", saved.UiLanguage);
        Assert.Equal("en", saved.DictationLanguage);
        // Only the measured silence level differs from what Echo ships with
        Assert.Equal([AdditionalSettings.SilenceThreshold], saved.Additional.Changed);
        Assert.Equal(Acceleration.Auto, saved.HardwareBackend);

        // English only, eight cores and 16 GB of memory: the small English model
        Assert.Equal("ggml-small.en.bin", saved.ModelName);
        Assert.Equal(HotkeyChoice.Default.Key, saved.HotkeyKey);
        Assert.Equal(0, saved.MicrophoneIndex);
        Assert.Equal("Desk microphone", saved.MicrophoneName);

        // Three times the measured background noise
        Assert.Equal(0.006, double.Parse(saved.Additional.Get(AdditionalSettings.SilenceThreshold)!, System.Globalization.CultureInfo.InvariantCulture), precision: 4);

        // English speech has nothing to be translated into, so the last step does not offer it.
        // The settings it does offer come in named blocks
        Assert.DoesNotContain("Translation into English", _console.Output);
        Assert.Contains("Recording sound", _console.Output);
        Assert.Contains(_console.Output.Split('\n'), line => line.Contains("║  Speech recognition "));
        Assert.Contains(_console.Output.Split('\n'), line => line.Contains("║  General "));

        // The silence level has a page that says what it is for, counts down the seconds of both
        // measurements, and shows the result before going on
        Assert.Contains("Echo starts recording only when the sound gets louder", _console.Output);
        Assert.Contains("Do you want to measure the silence level?", _console.Output);
        Assert.Contains("◉ Measure", _console.Output);
        Assert.Contains("○ Set it by hand", _console.Output);

        // Skipping is a key, as on every step, not a row of the list
        Assert.DoesNotContain("○ Skip", _console.Output);
        Assert.Contains("Enter confirm  ·  S skip  ·  Esc back", _console.Output);
        Assert.Contains("Measuring the silence level", _console.Output);
        Assert.Contains("Stay quiet, measuring the room: 1", _console.Output);
        Assert.Contains("Now speak at your normal volume: 1", _console.Output);
        Assert.Contains("The silence level is measured: 0.006", _console.Output);

        // The recognized phrase stands on its own: no mark in front of it, no line about the backend
        Assert.Contains(_console.Output.Split('\n'), line => line.Contains("║  0.42 s  hello from the test"));
        Assert.DoesNotContain("Running on", _console.Output);
        Assert.Equal(
            [
                "step 1 of 6 · Languages",
                "step 1 of 6 · Languages · Dictation language",
                "step 2 of 6 · Hardware acceleration",
                "step 3 of 6 · Model",
                "step 4 of 6 · Hotkey",
                "step 5 of 6 · Sound",
                "step 5 of 6 · Sound · Silence level",
                "step 5 of 6 · Sound · Test dictation",
                "step 6 of 6 · Additional settings",
                "review",
            ],
            ScreensShown());

        Assert.Contains("hello from the test", _console.Output);

        _downloader.Verify(
            downloader => downloader.DownloadAsync(
                It.Is<DownloadRequest>(request => request.Url.AbsoluteUri.EndsWith("ggml-small.en.bin")),
                It.IsAny<string>(),
                It.IsAny<IProgress<DownloadProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [WindowsFact]
    public async Task RunAsync_CudaLibrariesMissing_CopiesTheOnesPickedInFileChooser()
    {
        // Arrange: the user keeps the libraries in a folder of their own, next to a file that is not one of them
        string downloads = Path.Combine(_home, "downloads");
        Directory.CreateDirectory(downloads);

        string stranger = Path.Combine(downloads, "cudart64_12.dll");
        File.WriteAllText(stranger, "an older version");

        foreach (string library in _libraries)
            File.WriteAllText(Path.Combine(downloads, library), library);

        _hardware = new HardwareInfo(_modernGpu, IsVulkanAvailable: true, MemoryGb: 32, ProcessorCores: 16);
        TrackLibrariesInFolder();
        _hotkeys.Presses.Enqueue([new HotkeyChoice("VcF8", "leftctrl")]);

        _shell.SetupGet(shell => shell.CanPickFiles).Returns(true);
        _shell.Setup(shell => shell.PickFilesAsync(It.IsAny<string>(), string.Join(';', _libraries), It.IsAny<CancellationToken>()))
            .ReturnsAsync([stranger, .. _libraries.Select(library => Path.Combine(downloads, library))]);

        _keys.Press(ConsoleKey.Enter)                                               // interface language: English
            .Press(ConsoleKey.DownArrow).Press(ConsoleKey.Enter)                    // dictation language: Russian
            .Press(ConsoleKey.Enter)                                                // hardware: automatic, that is CUDA
            .Press(ConsoleKey.DownArrow).Press(ConsoleKey.Enter)                    // libraries: I already have them
            .Press(ConsoleKey.Enter)                                                // "not one of the libraries": continue
            .Press(ConsoleKey.Enter)                                                // model: the recommended one
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)            // key: confirm the pressed one
            .PressWhen(ConsoleKey.DownArrow, () => _microphones.IsEveryDeviceHeard) // input device: move to the second one
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // and take it
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)        // test dictation: finish
            .Press(ConsoleKey.Enter)                                                // additional settings: translation on, the first setting
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: round to the row under the list
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        UserSettings saved = CreateStore().Load();

        Assert.Equal(0, _keys.Remaining);
        Assert.Equal("ru", saved.DictationLanguage);
        Assert.True(saved.Additional.IsOn(AdditionalSettings.Translate));
        Assert.Equal(Acceleration.Auto, saved.HardwareBackend);

        // Translation was turned on after the turbo model had been chosen, and that model cannot translate:
        // the user is told so right there
        Assert.Equal("ggml-large-v3-turbo.bin", saved.ModelName);
        Assert.Contains("large-v3-turbo, cannot translate", _console.Output);
        Assert.Equal("VcF8", saved.HotkeyKey);
        Assert.Equal("leftctrl", saved.HotkeyModifier);
        Assert.Equal(1, saved.MicrophoneIndex);
        Assert.Equal("Headset", saved.MicrophoneName);

        // The libraries are copied, the originals stay, and the file with another name is left alone
        Assert.Equal(_libraries.Order(), Directory.GetFiles(_cuda.Object.Directory).Select(Path.GetFileName).Order());
        Assert.All(_libraries, library => Assert.True(File.Exists(Path.Combine(downloads, library))));
        Assert.Contains("cudart64_12.dll", _console.Output);

        // The page lists what is missing, and stays open until nothing is
        Assert.Contains("× cublasLt64_13.dll", _console.Output);
        Assert.DoesNotContain("Use Vulkan", _console.Output);

        // The page inside the hardware step is named in the navigation while it is open, and only then
        Assert.Equal(
            [
                "step 2 of 6 · Hardware acceleration",
                "step 2 of 6 · Hardware acceleration · NVIDIA libraries",
                "step 3 of 6 · Model",
            ],
            ScreensShown().SkipWhile(screen => !screen.Contains("Hardware")).Take(3));

        // The line of steps itself never grows a page: it names the five steps and what was chosen in each
        Assert.DoesNotContain("›  NVIDIA libraries", _console.Output);
        Assert.Contains("√ 1 Languages (en/ru)  ›  2 Hardware acceleration (CUDA)  ›  3 Model  ›", _console.Output);
        Assert.Contains("√ 2 Hardware acceleration (CUDA)  ›  √ 3 Model (large-v3-turbo)  ›", _console.Output);
        Assert.Contains("4 Hotkey (Left Ctrl + F8)", _console.Output);

        // A key pressed but not yet confirmed was already named there; the steps ahead show nothing
        Assert.Contains("3 Model  ›  4 Hotkey  ›  5 Sound", _console.Output);
        Assert.Contains("√ 5 Sound (Headset)", _console.Output);

        // The stand-in pipeline ran on the processor, so the user is told CUDA needs a restart
        Assert.Contains("restarted", _console.Output);

        _cuda.Verify(
            cuda => cuda.InstallAsync(It.IsAny<IProgress<DownloadProgress>>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [WindowsFact]
    public async Task RunAsync_CudaLibrariesDownloaded_MarksEachOneAsItArrives()
    {
        // Arrange: the first library arrives well before the other two
        _hardware = new HardwareInfo(_modernGpu, IsVulkanAvailable: true, MemoryGb: 32, ProcessorCores: 16);
        TrackLibrariesInFolder();

        _cuda.Setup(cuda => cuda.InstallAsync(It.IsAny<IProgress<DownloadProgress>>(), It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IProgress<DownloadProgress> _, IProgress<string> downloaded, CancellationToken cancellationToken) =>
            {
                downloaded.Report(_libraries[0]);

                await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);

                Directory.CreateDirectory(_cuda.Object.Directory);

                foreach (string library in _libraries)
                    File.WriteAllText(Path.Combine(_cuda.Object.Directory, library), library);
            });

        _keys.Press(ConsoleKey.Enter)                                               // interface language
            .Press(ConsoleKey.Enter)                                                // dictation language
            .Press(ConsoleKey.Enter)                                                // hardware: automatic, that is CUDA
            .Press(ConsoleKey.Enter)                                                // libraries: download them now
            .Press(ConsoleKey.Enter)                                                // model
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        Assert.Equal(0, _keys.Remaining);

        string[] lines = _console.Output.Split('\n');
        int arrived = Array.FindIndex(lines, line => line.Contains("√ cudart64_13.dll"));

        Assert.True(arrived > 0);
        Assert.Contains("× cublas64_13.dll", lines[arrived + 1]);
        Assert.Contains("× cublasLt64_13.dll", lines[arrived + 2]);
    }

    [Fact]
    public async Task RunAsync_EscapeDuringDownload_StopsItAndGoesBackToTheListOfModels()
    {
        // Arrange: the first two attempts go on until they are stopped, the third one finishes at once
        int attempts = 0;

        _downloader
            .Setup(downloader => downloader.DownloadAsync(
                It.IsAny<DownloadRequest>(), It.IsAny<string>(), It.IsAny<IProgress<DownloadProgress>>(), It.IsAny<CancellationToken>()))
            .Returns((DownloadRequest _, string _, IProgress<DownloadProgress> _, CancellationToken cancellationToken) =>
                Interlocked.Increment(ref attempts) < 3 ? Task.Delay(Timeout.Infinite, cancellationToken) : Task.CompletedTask);

        _keys.Press(ConsoleKey.Enter)                                               // interface language
            .Press(ConsoleKey.Enter)                                                // dictation language
            .Press(ConsoleKey.Enter)                                                // hardware
            .Press(ConsoleKey.Enter)                                                // model: download the recommended one
            .PressWhen(ConsoleKey.Escape, () => Volatile.Read(ref attempts) == 1)   // stop the download: back to the list of models
            .Press(ConsoleKey.Enter)                                                // model: download it again
            .PressWhen(ConsoleKey.Escape, () => Volatile.Read(ref attempts) == 2)   // stop it again
            .Press(ConsoleKey.Enter)                                                // model: download after all
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        Assert.Equal(0, _keys.Remaining);
        Assert.Equal(3, attempts);
        Assert.Equal("ggml-small.en.bin", CreateStore().Load().ModelName);

        // The list of models took the Enter after each Escape: no screen in between asks to continue.
        // Stopping a download is not a step back: the wizard never left the model step
        Assert.Equal(
            ["step 2 of 6 · Hardware acceleration", "step 3 of 6 · Model", "step 4 of 6 · Hotkey"],
            ScreensShown().SkipWhile(screen => !screen.Contains("Hardware")).Take(3));
    }

    [WindowsFact]
    public async Task RunAsync_EscapeOnAnyScreen_LeadsToTheScreenShownJustBefore()
    {
        // Arrange
        _hardware = new HardwareInfo(_modernGpu, IsVulkanAvailable: true, MemoryGb: 32, ProcessorCores: 16);
        _cuda.Setup(cuda => cuda.FindMissingLibraries()).Returns(["cublasLt64_13.dll"]);

        _keys.Press(ConsoleKey.Enter)                                               // interface language
            .Press(ConsoleKey.Enter)                                                // dictation language
            .Press(ConsoleKey.Escape)                                               // hardware -> dictation language
            .Press(ConsoleKey.Escape)                                               // dictation language -> interface language
            .Press(ConsoleKey.Enter)                                                // interface language
            .Press(ConsoleKey.Enter)                                                // dictation language
            .Press(ConsoleKey.Enter)                                                // hardware: automatic, that is CUDA
            .Press(ConsoleKey.Escape)                                               // libraries -> hardware
            .Press(ConsoleKey.DownArrow).Press(ConsoleKey.DownArrow)                
            .Press(ConsoleKey.Enter)                                                // hardware: Vulkan, which needs no libraries
            .Press(ConsoleKey.Escape)                                               // model -> hardware
            .Press(ConsoleKey.Escape)                                               // hardware -> dictation language
            .Press(ConsoleKey.Enter)                                                // dictation language
            .Press(ConsoleKey.Enter)                                                // hardware: Vulkan is remembered
            .Press(ConsoleKey.Enter)                                                // model
            .PressWhen(ConsoleKey.Escape, () => _hotkeys.StartCount == 1)           // key -> model
            .Press(ConsoleKey.Enter)                                                // model
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 2)            // key
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Escape, () => _dictation.StartCount == 1 && _dictation.IsPhraseDelivered) // test -> silence level
            .Press(ConsoleKey.Escape)                                               // silence level -> input device
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Escape)                                               // the result -> the measurement itself
            .PressWhen(ConsoleKey.Escape, () => _microphones.IsListeningTo(1))      // the measurement -> the question
            .Press(ConsoleKey.S)                                                    // silence level: skip, the measured one stays
            .PressWhen(ConsoleKey.Enter, () => _dictation.StartCount == 2 && _dictation.IsPhraseDelivered)  // test: finish
            .Press(ConsoleKey.Escape)                                               // additional settings -> test dictation
            .PressWhen(ConsoleKey.Enter, () => _dictation.StartCount == 3 && _dictation.IsPhraseDelivered)  // test: finish
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Escape)                                               // review -> additional settings
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        Assert.Equal(0, _keys.Remaining);
        Assert.Equal(
            [
                "step 1 of 6 · Languages",
                "step 1 of 6 · Languages · Dictation language",
                "step 2 of 6 · Hardware acceleration",
                "step 1 of 6 · Languages · Dictation language",
                "step 1 of 6 · Languages",
                "step 1 of 6 · Languages · Dictation language",
                "step 2 of 6 · Hardware acceleration",
                "step 2 of 6 · Hardware acceleration · NVIDIA libraries",
                "step 2 of 6 · Hardware acceleration",
                "step 3 of 6 · Model",
                "step 2 of 6 · Hardware acceleration",
                "step 1 of 6 · Languages · Dictation language",
                "step 2 of 6 · Hardware acceleration",
                "step 3 of 6 · Model",
                "step 4 of 6 · Hotkey",
                "step 3 of 6 · Model",
                "step 4 of 6 · Hotkey",
                "step 5 of 6 · Sound",
                "step 5 of 6 · Sound · Silence level",
                "step 5 of 6 · Sound · Test dictation",
                "step 5 of 6 · Sound · Silence level",
                "step 5 of 6 · Sound",
                "step 5 of 6 · Sound · Silence level",
                "step 5 of 6 · Sound · Test dictation",
                "step 6 of 6 · Additional settings",
                "step 5 of 6 · Sound · Test dictation",
                "step 6 of 6 · Additional settings",
                "review",
                "step 6 of 6 · Additional settings",
                "review",
            ],
            ScreensShown());

        Assert.Equal("Vulkan", CreateStore().Load().HardwareBackend);
    }

    [WindowsFact]
    public async Task RunAsync_SeveralGraphicsCards_AsksWhichOneToUse()
    {
        // Arrange
        var second = new NvidiaGpu("NVIDIA GeForce RTX 4060", 8188, new Version(617, 14), new Version(8, 9), Index: 1);

        _hardware = new HardwareInfo(_modernGpu, IsVulkanAvailable: true, MemoryGb: 32, ProcessorCores: 16)
        {
            NvidiaCards = [_modernGpu, second]
        };

        _keys.Press(ConsoleKey.Enter)                                               // interface language
            .Press(ConsoleKey.Enter)                                                // dictation language
            .Press(ConsoleKey.Enter)                                                // hardware: automatic, that is CUDA
            .Press(ConsoleKey.DownArrow).Press(ConsoleKey.Enter)                    // graphics card: the second one
            .Press(ConsoleKey.Enter)                                                // model
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        UserSettings saved = CreateStore().Load();

        Assert.Equal(0, _keys.Remaining);
        Assert.Equal(1, saved.GraphicsCardIndex);
        Assert.Contains("step 2 of 6 · Hardware acceleration · Graphics card", ScreensShown());

        // The recommendation follows the chosen card: 8 GB still carries the turbo model
        Assert.Equal("ggml-large-v3-turbo.bin", saved.ModelName);
        Assert.Contains("NVIDIA GeForce RTX 4060", _console.Output);
    }

    [Fact]
    public async Task RunAsync_ModelPickedInFileChooser_IsCopiedIntoModelsFolderAndUsed()
    {
        // Arrange: one real-looking model and one file that only has the right extension
        string downloads = Path.Combine(_home, "downloads");
        Directory.CreateDirectory(downloads);

        string model = Path.Combine(downloads, "ggml-my-own.bin");
        string notModel = Path.Combine(downloads, "notes.bin");

        File.WriteAllBytes(model, [.. "lmgg"u8, .. new byte[5000]]);
        File.WriteAllText(notModel, "just some notes");

        _shell.SetupGet(shell => shell.CanPickFiles).Returns(true);
        _shell.Setup(shell => shell.PickFilesAsync(It.IsAny<string>(), "*.bin", It.IsAny<CancellationToken>()))
            .ReturnsAsync([notModel, model]);

        _keys.Press(ConsoleKey.Enter)                                               // interface language
            .Press(ConsoleKey.Enter)                                                // dictation language
            .Press(ConsoleKey.Enter)                                                // hardware
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow)
            .Press(ConsoleKey.Enter)                                                // model: I already have downloaded models
            .Press(ConsoleKey.Enter)                                                // "notes.bin is not a model": continue
            .Press(ConsoleKey.Enter)                                                // model: the copied file is highlighted
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        string models = new AppPaths(_home, _home).ModelsDirectory;

        Assert.Equal(0, _keys.Remaining);
        Assert.Equal("ggml-my-own.bin", CreateStore().Load().ModelName);
        Assert.Equal(File.ReadAllBytes(model), File.ReadAllBytes(Path.Combine(models, "ggml-my-own.bin")));
        Assert.Equal(["ggml-my-own.bin"], Directory.GetFiles(models).Select(Path.GetFileName));

        // The original stays where the user keeps it
        Assert.True(File.Exists(model));
        Assert.Contains("notes.bin", _console.Output);

        // The copied file is on the list of models itself, marked like any downloaded one; no other page opened
        Assert.Contains(_console.Output.Split('\n'), line => line.Contains("ggml-my-own.bin") && line.Contains("Already downloaded"));
        Assert.DoesNotContain(ScreensShown(), screen => screen.StartsWith("step 3 of 6 · Model ·"));

        _downloader.Verify(
            downloader => downloader.DownloadAsync(
                It.IsAny<DownloadRequest>(), It.IsAny<string>(), It.IsAny<IProgress<DownloadProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_LineChosenOnReviewScreen_RunsThatStepAgainAndKeepsTheRest()
    {
        // Arrange
        _hotkeys.Presses.Enqueue([]);
        _hotkeys.Presses.Enqueue([new HotkeyChoice("VcF9", string.Empty)]);

        _keys.Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow)
            .Press(ConsoleKey.UpArrow)                                              // review: up to the key line
            .Press(ConsoleKey.Enter)
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 2)            // key: confirm the newly pressed one
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        UserSettings saved = CreateStore().Load();

        Assert.Equal(0, _keys.Remaining);
        Assert.Equal("VcF9", saved.HotkeyKey);
        Assert.Equal("ggml-small.en.bin", saved.ModelName);
        Assert.Equal(1, _dictation.StartCount);
    }

    [Fact]
    public async Task RunAsync_TerminalWithFewRows_KeepsNavigationOnScreen()
    {
        // Arrange: a screen taller than the terminal loses its top, and the navigation with it
        _console.Height(20);

        _keys.Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert: the mark the console puts in place of cut-off rows never appeared
        Assert.DoesNotContain(_console.Output.Split('\n'), line => line.Trim() == "…");
        Assert.Contains("...", _console.Output);
    }

    [Fact]
    public async Task RunAsync_ListOfChoices_TakesOneLinePerChoice()
    {
        // Arrange
        _keys.Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert: on the model screen the choices follow each other with nothing in between
        string[] lines = _console.Output.Split('\n');
        int tiny = Array.FindIndex(lines, line => line.Contains("○ tiny.en"));

        Assert.True(tiny > 0);
        Assert.Contains("○ base.en", lines[tiny + 1]);
        Assert.Contains("small.en", lines[tiny + 2]);
        Assert.Contains("○ medium.en", lines[tiny + 3]);

        // The explanation of the highlighted choice is under the list
        Assert.Contains("Fast with good accuracy", _console.Output);
        Assert.DoesNotContain("makes the most mistakes", _console.Output);
    }

    [Fact]
    public async Task RunAsync_ModelList_MarksTinyAndBaseAsNotRecommended()
    {
        // Arrange
        _keys.Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert: the two smallest models say so on their line; the suggested one does not
        string[] lines = _console.Output.Split('\n');
        int tiny = Array.FindIndex(lines, line => line.Contains("○ tiny.en"));

        Assert.Contains("Not recommended", lines[tiny]);
        Assert.Contains("Not recommended", lines[tiny + 1]);
        Assert.DoesNotContain("Not recommended", lines[tiny + 2]);
        Assert.Contains("Recommended", lines[tiny + 2]);
    }

    [Fact]
    public async Task RunAsync_NarrowTerminal_BreaksTheLineOfStepsOnlyBetweenSteps()
    {
        // Arrange
        _console.Width(60);

        _keys.Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert: every row of the line starts with a whole step, never with the tail of one
        string[] lines = _console.Output.Split('\n');
        int first = Array.FindLastIndex(lines, line => line.Contains("√ 1 Languages (en/en)"));

        Assert.True(first > 0);
        Assert.EndsWith("›", lines[first].TrimEnd());
        Assert.Matches(@"^  √ \d ", lines[first + 1]);
        Assert.DoesNotContain(lines, line => line.TrimStart().StartsWith("Sound") || line.TrimStart().StartsWith("acceleration"));
    }

    [Fact]
    public async Task RunAsync_AdditionalSettings_SpaceStepsThroughValuesAndEnterOpensThemForTyping()
    {
        // Arrange
        ReachAdditionalSettings()
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow)                    // round from the first setting to the last one
            .Press(ConsoleKey.Spacebar)                                             // automatic updates: on
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Spacebar)                   // log detail: from Debug on to Information
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Spacebar)                   // dictation history: off
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Spacebar)                   // automatic insertion: off
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // sound volume: open the field, which holds "10"
            .Type("00").Press(ConsoleKey.Enter)                                     // "1000" is out of range: the field stays open
            .Press(ConsoleKey.Backspace).Press(ConsoleKey.Backspace)
            .Press(ConsoleKey.Backspace).Press(ConsoleKey.Backspace)
            .Type("35").Press(ConsoleKey.Enter)                                     // sound volume: 35 percent
            .Press(ConsoleKey.Spacebar)                                             // sound volume: on to the next step, 50 percent
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Spacebar)                   // recording sound: off
            .Press(ConsoleKey.Enter)                                                // recording sound: on again, Enter flips it too
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow)
            .Press(ConsoleKey.Enter).Type("7").Press(ConsoleKey.Escape)             // processor threads: typed, then abandoned
            .Press(ConsoleKey.Spacebar).Press(ConsoleKey.Spacebar)                  // processor threads: from "auto" to 1, then to 2
            .Press(ConsoleKey.UpArrow)
            .Type(" ").Type(" ").Type(" ").Type(" ").Type(" ").Type(" ").Type(" ")  // temperature: all the way round, back to "auto"
            .Type(" ")                                                              // temperature: 0, by a space without a key code
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow)
            .Press(ConsoleKey.Spacebar).Type("gRPC, Echo").Press(ConsoleKey.Enter)  // vocabulary: there is nothing to step through, so it opens
            .Press(ConsoleKey.UpArrow)
            .Press(ConsoleKey.Enter)                                                // continue
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        UserSettings saved = CreateStore().Load();

        Assert.Equal(0, _keys.Remaining);
        Assert.Equal(
            [
                AdditionalSettings.Prompt,
                AdditionalSettings.Temperature,
                AdditionalSettings.Threads,
                AdditionalSettings.SilenceThreshold,
                AdditionalSettings.Volume,
                AdditionalSettings.AutoInsert,
                AdditionalSettings.Memory,
                AdditionalSettings.LogLevel,
                AdditionalSettings.AutoUpdate,
            ],
            saved.Additional.Changed);

        Assert.Equal("gRPC, Echo", saved.Additional.Get(AdditionalSettings.Prompt));
        Assert.Equal("0", saved.Additional.Get(AdditionalSettings.Temperature));
        Assert.Equal("2", saved.Additional.Get(AdditionalSettings.Threads));
        Assert.Equal("0.5", saved.Additional.Get(AdditionalSettings.Volume));
        Assert.False(saved.Additional.IsOn(AdditionalSettings.AutoInsert));
        Assert.False(saved.Additional.IsOn(AdditionalSettings.Memory));
        Assert.True(saved.Additional.IsOn(AdditionalSettings.PlaySound));
        Assert.Equal("Information", saved.Additional.Get(AdditionalSettings.LogLevel));
        Assert.True(saved.Additional.IsOn(AdditionalSettings.AutoUpdate));

        Assert.Contains("Enter a whole number from 0 to 100", _console.Output);

        // The line of keys says what the highlighted row answers to
        Assert.Contains("Space switch  ·  Enter type a value  ·  Esc back", _console.Output);
        Assert.Contains("↑↓ select  ·  Space switch  ·  Esc back", _console.Output);
        Assert.Contains("↑↓ select  ·  Enter type a value  ·  Esc back", _console.Output);
        Assert.Contains("↑↓ select  ·  Enter continue  ·  Esc back", _console.Output);
    }

    [Fact]
    public async Task RunAsync_ReviewScreen_ListsOnlyTheMainChoices()
    {
        // Arrange: something is changed on the last step, and still it is not listed
        ReachAdditionalSettings()
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.UpArrow)                    // round from the first setting to the last one
            .Press(ConsoleKey.Spacebar)                                             // log detail: Information
            .Press(ConsoleKey.DownArrow).Press(ConsoleKey.Enter)                    // continue
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        string review = _console.Output[_console.Output.LastIndexOf("Is everything right?", StringComparison.Ordinal)..];

        string[] rows = [.. Regex.Matches(review, @"[○◉] (.+?)\s{2,}").Select(match => match.Groups[1].Value)];

        Assert.Equal(
            ["Interface language", "Dictation language", "Hardware acceleration", "Model", "Push-to-talk key", "Microphone", "Additional settings", "Save and start"],
            rows);
    }

    [Fact]
    public async Task RunAsync_InputDevicePage_ShowsHowLoudEveryDeviceIs()
    {
        // Arrange: the desk microphone hears the user, the headset hears nothing
        _microphones.SteadyLevels[0] = 0.3f;
        _microphones.SteadyLevels[1] = 0f;

        int openedOnDevicePage = -1;

        _keys.Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)
            .PressWhen(ConsoleKey.Enter, () =>                                      // input device: the first one, once both are heard
            {
                openedOnDevicePage = _microphones.OpenCount;

                return _microphones.IsEveryDeviceHeard;
            })
            .Press(ConsoleKey.S)                                                    // silence level: skip
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert: both devices were listened to at once, and both are closed again
        Assert.Equal(0, _keys.Remaining);
        Assert.Equal(2, openedOnDevicePage);
        Assert.True(_microphones.IsListeningTo(0));
        Assert.Equal("Desk microphone", CreateStore().Load().MicrophoneName);

        // Every device has its level next to its name: the one that hears the user rises, the other stays flat
        string[] page = FramesOf("step 5 of 6 · Sound").Last(frame => frame.Any(row => row.Contains("Choose the input device")));

        Assert.Contains(page, row => Regex.IsMatch(row, "◉ Desk microphone +▁*█+ +║"));
        Assert.Contains(page, row => Regex.IsMatch(row, "○ Headset +▁{16} +║"));
    }

    [Fact]
    public async Task RunAsync_SilenceLevelSetByHand_KeepsTheTypedNumberWithoutMeasuring()
    {
        // Arrange
        _keys.Press(ConsoleKey.Enter)                                               // interface language: English
            .Press(ConsoleKey.Enter)                                                // dictation language: English
            .Press(ConsoleKey.Enter)                                                // hardware: automatic
            .Press(ConsoleKey.Enter)                                                // model: the recommended one
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)            // key: keep the default
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.DownArrow).Press(ConsoleKey.Enter)                    // silence level: set it by hand, the field holds "0.01"
            .Press(ConsoleKey.Backspace).Press(ConsoleKey.Backspace)
            .Press(ConsoleKey.Backspace).Press(ConsoleKey.Backspace)
            .Type("2").Press(ConsoleKey.Enter)                                      // "2" is out of range: the field stays open
            .Press(ConsoleKey.Backspace)
            .Type("0,05").Press(ConsoleKey.Enter)                                   // a comma works as well as a point
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)        // test dictation: finish
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert: the typed level is kept, and nothing was measured
        Assert.Equal(0, _keys.Remaining);
        Assert.Equal("0.05", CreateStore().Load().Additional.Get(AdditionalSettings.SilenceThreshold));
        Assert.Contains("Enter a number from 0 to 1", _console.Output);
        Assert.DoesNotContain("Stay quiet", _console.Output);

        // Only the list of devices opened the microphones; the silence level page did not
        Assert.Equal(2, _microphones.OpenCount);
    }

    [Fact]
    public async Task RunAsync_AdditionalSettingsInShortTerminal_OpenOnTheFirstSettingAndKeepTheLastRowInPlace()
    {
        // Arrange: Russian speech, so that translation, the very first setting, is offered
        _console.Height(30);

        _keys.Press(ConsoleKey.Enter)                                               // interface language: English
            .Press(ConsoleKey.DownArrow).Press(ConsoleKey.Enter)                    // dictation language: Russian
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.Enter)
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)
            .Press(ConsoleKey.UpArrow)                                              // additional settings: round to the row that leads onwards
            .Press(ConsoleKey.UpArrow)                                              // to the last setting
            .Press(ConsoleKey.DownArrow).Press(ConsoleKey.Enter)                    // onto the row that leads onwards again
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        Assert.Equal(0, _keys.Remaining);
        Assert.DoesNotContain(_console.Output.Split('\n'), line => line.Trim() == "…");

        List<string[]> frames = FramesOf("step 6 of 6");

        // The list opens on its first setting, highlighted, with the row that leads onwards under it
        string[] opened = frames[0];
        int heading = Array.FindIndex(opened, line => line.Contains("║  Speech recognition "));

        Assert.True(heading > 0);
        Assert.DoesNotContain(opened[..heading], line => line.Contains("..."));
        Assert.Matches(@"║ +║", opened[heading + 1]);
        Assert.Contains("◉ Translation into English", opened[heading + 2]);
        Assert.Contains(opened, line => line.Contains("○ Continue"));

        // The explanation of translation ends with its warning, on a line of its own
        Assert.Contains(_console.Output.Split('\n'), line => line.Contains("║  The large-v3-turbo model cannot translate"));

        // Scrolled to its end, the list leaves the row that leads onwards where it was
        string[] scrolled = frames[2];

        Assert.Contains(scrolled, line => line.Contains("◉ Download updates automatically"));
        Assert.DoesNotContain(scrolled, line => line.Contains("Translation into English"));
        Assert.Equal(
            Array.FindIndex(opened, line => line.Contains("Continue")),
            Array.FindIndex(scrolled, line => line.Contains("Continue")));
    }

    [Fact]
    public async Task SelectAsync_ListLongerThanTheTerminal_NamesTheBlockAboveTheMarkOfItsHiddenRows()
    {
        // Arrange: two blocks of eight rows and a row set apart, in a terminal with room for a few of them
        _console.Height(24);

        var ui = new SetupUi(new ScriptedConsole(_console, _keys), Theme.Full, TimeSpan.FromMilliseconds(1));

        List<Choice<string>> choices =
        [
            .. "ab".SelectMany(block => Enumerable.Range(1, 8).Select(row => new Choice<string>($"{block}{row}", $"{block}{row}")
            {
                Group = $"Block {block}"
            })),
            new("go", "Go on") { IsSetApart = true },
        ];

        // Down through every row, and round to the row set apart
        for (int press = 0; press < choices.Count; press++)
            _keys.Press(ConsoleKey.DownArrow);

        _keys.Press(ConsoleKey.Enter);

        // Act
        UiResult<string> picked = await ui.SelectAsync(
            new SetupScreen(0, ["Only"], CanGoBack: false),
            "Pick one",
            choices,
            selectedIndex: choices.Count - 1,
            notes: null,
            CancellationToken.None);

        // Assert
        Assert.Equal("go", picked.Value);

        List<string[]> frames = FramesOf("step 1 of 1");

        // The list opens on its first row although the highlighted one is the row set apart under it
        int start = Array.FindIndex(frames[0], line => line.Contains("║  Block a "));

        Assert.Contains("○ a1", frames[0][start + 2]);

        int namedAboveMark = 0;

        foreach (string[] frame in frames)
        {
            // The row set apart never moves
            Assert.Equal(
                Array.FindIndex(frames[0], line => line.Contains("Go on")),
                Array.FindIndex(frame, line => line.Contains("Go on")));

            for (int index = 0; index < frame.Length; index++)
            {
                if (!frame[index].Contains("║      ...  "))
                    continue;

                System.Text.RegularExpressions.Match rowBelow = ListRow().Match(frame[index + 1]);

                if (rowBelow.Success && rowBelow.Groups[2].Value != "1")
                {
                    // Rows of this block are hidden above: its name stays, and the mark is under it
                    Assert.Contains($"║  Block {rowBelow.Groups[1].Value} ", frame[index - 2]);
                    Assert.Matches(@"║ +║", frame[index - 1]);

                    namedAboveMark++;
                }
                else if (!rowBelow.Success && frame[index + 2].Contains("║  Block "))
                {
                    // Only another block is hidden above: the mark stands for it, over the name of this one
                    Assert.Matches(@"[○◉] [ab]1 ", frame[index + 4]);
                    Assert.DoesNotContain("║  Block ", frame[index - 2]);
                }
                else
                {
                    // The mark of rows hidden below follows a row
                    Assert.Matches(ListRow(), frame[index - 1]);
                }
            }
        }

        Assert.True(namedAboveMark > 0);
    }

    [GeneratedRegex(@"[○◉] ([ab])(\d) ")]
    private static partial Regex ListRow();

    [Fact]
    public async Task RunAsync_SetupAlreadyCompleted_HasNoNewStepsToShow()
    {
        // Arrange
        CreateStore().Save(new UserSettings { SetupVersion = SetupRunner.CurrentVersion });

        SetupRunner runner = CreateRunner();

        // Act
        SetupOutcome outcome = await runner.RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert
        Assert.Equal(SetupOutcome.NothingToDo, outcome);
        Assert.False(runner.IsSetupRequired());
    }

    [Fact]
    public async Task RunAsync_SetupCompleted_OpensOnTheReviewScreenAndRunsOnlyThePickedStep()
    {
        // Arrange
        SaveCompletedSetup(AccelerationBackend.Cpu);
        _hotkeys.Presses.Enqueue([new HotkeyChoice("VcF9", string.Empty)]);

        _keys.Press(ConsoleKey.DownArrow).Press(ConsoleKey.DownArrow)
            .Press(ConsoleKey.DownArrow).Press(ConsoleKey.DownArrow)                // review: down to the key line
            .Press(ConsoleKey.Enter)
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)            // key: confirm the newly pressed one
            .Press(ConsoleKey.DownArrow).Press(ConsoleKey.DownArrow)
            .Press(ConsoleKey.DownArrow)                                            // review: from the key line down to save
            .Press(ConsoleKey.Enter);

        // Act
        SetupOutcome outcome = await CreateRunner().RunAsync(onlyNewSteps: false, CancellationToken.None);

        // Assert
        UserSettings saved = CreateStore().Load();

        Assert.Equal(SetupOutcome.Completed, outcome);
        Assert.Equal(0, _keys.Remaining);
        Assert.Equal(["review", "step 4 of 6 · Hotkey", "review"], ScreensShown());
        Assert.Equal("VcF9", saved.HotkeyKey);
        Assert.Equal("ggml-small.en.bin", saved.ModelName);
        Assert.Equal("USB Microphone", saved.MicrophoneName);
        Assert.Contains("Choose what to change", _console.Output);
    }

    [Fact]
    public async Task RunAsync_SetupCompletedAndLeftWithEscape_SavesNothing()
    {
        // Arrange
        SaveCompletedSetup(AccelerationBackend.Cpu);
        _hotkeys.Presses.Enqueue([new HotkeyChoice("VcF9", string.Empty)]);

        _keys.Press(ConsoleKey.DownArrow).Press(ConsoleKey.DownArrow)
            .Press(ConsoleKey.DownArrow).Press(ConsoleKey.DownArrow)                // review: down to the key line
            .Press(ConsoleKey.Enter)
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)            // key: confirm the newly pressed one
            .Press(ConsoleKey.Escape);                                              // review: leave

        // Act
        SetupOutcome outcome = await CreateRunner().RunAsync(onlyNewSteps: false, CancellationToken.None);

        // Assert
        Assert.Equal(SetupOutcome.Cancelled, outcome);
        Assert.Equal(0, _keys.Remaining);
        Assert.Equal("VcF24", CreateStore().Load().HotkeyKey);
        Assert.Contains("Esc leave without saving", _console.Output);
    }

    [Theory]
    [InlineData(RuntimeLibrary.Cuda, true)]
    [InlineData(RuntimeLibrary.Vulkan, false)]
    [InlineData(null, false)]
    public async Task RunAsync_VulkanHighlighted_WarnsOnlyWhenTheLoadedRuntimeCannotRunIt(RuntimeLibrary? loaded, bool isWarned)
    {
        // Arrange: the list opens on the processor, which any runtime can run on
        _hardware = new HardwareInfo(_modernGpu, IsVulkanAvailable: true, MemoryGb: 32, ProcessorCores: 16);
        _loadedRuntime = loaded;
        SaveCompletedSetup(AccelerationBackend.Cpu);

        _keys.Press(ConsoleKey.DownArrow).Press(ConsoleKey.DownArrow)               // review: down to the acceleration line
            .Press(ConsoleKey.Enter)
            .Press(ConsoleKey.UpArrow)                                              // hardware: from the processor up to Vulkan
            .Press(ConsoleKey.Escape)                                               // hardware -> review
            .Press(ConsoleKey.Escape);                                              // review: leave

        // Act
        await CreateRunner().RunAsync(onlyNewSteps: false, CancellationToken.None);

        // Assert
        Assert.Equal(0, _keys.Remaining);
        Assert.Equal(isWarned, _console.Output.Contains("Takes effect only after Echo restarts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_SystemLacksAProgram_SaysWhatIsMissingAndLooksAgainOnRequest()
    {
        // Arrange: the clipboard program is missing at first and installed while the page is open
        _requirements.SetupSequence(requirements => requirements.Check())
            .Returns(
            [
                new SystemRequirement("Sound server", IsMet: true, "No sound without it.", Fix: null),
                new SystemRequirement("Clipboard program (xsel)", IsMet: false, "No clipboard without it.", "Install: sudo apt install xsel"),
            ])
            .Returns([new SystemRequirement("Clipboard program (xsel)", IsMet: true, "No clipboard without it.", "Install: sudo apt install xsel")]);

        _keys.Press(ConsoleKey.Enter)                                               // interface language: English
            .Press(ConsoleKey.Enter)                                                // dictation language: English
            .Press(ConsoleKey.R)                                                    // system check: look again
            .Press(ConsoleKey.Enter)                                                // system check: go on
            .Press(ConsoleKey.Enter)                                                // hardware: automatic
            .Press(ConsoleKey.Enter)                                                // model: the recommended one
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)            // key: keep the default
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)        // test dictation: finish
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        SetupOutcome outcome = await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert: what is missing, what it is needed for and the command that installs it
        Assert.Equal(SetupOutcome.Completed, outcome);
        Assert.Contains("Clipboard program (xsel)", _console.Output);
        Assert.Contains("No clipboard without it.", _console.Output);
        Assert.Contains("Install: sudo apt install xsel", _console.Output);
        Assert.Contains("step 2 of 6 · Hardware acceleration · What this system needs", ScreensShown());
        _requirements.Verify(requirements => requirements.Check(), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_SystemLacksAPackage_EnterInstallsItInTheTerminalAndLooksAgain()
    {
        // Arrange: the clipboard program is missing, and Echo knows how this system installs it
        string[] command = ["sudo", "apt-get", "install", "-y", "xsel"];

        _requirements.SetupSequence(requirements => requirements.Check())
            .Returns([new SystemRequirement("Clipboard program (xsel)", IsMet: false, "No clipboard without it.", "Install: sudo apt install xsel", Package: "xsel")])
            .Returns([new SystemRequirement("Clipboard program (xsel)", IsMet: true, "No clipboard without it.", "Install: sudo apt install xsel", Package: "xsel")]);

        _requirements.Setup(requirements => requirements.InstallCommand(It.Is<IReadOnlyList<string>>(packages => packages.SequenceEqual(new[] { "xsel" }))))
            .Returns(command);

        _shell.Setup(shell => shell.RunInTerminalAsync(command, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _keys.Press(ConsoleKey.Enter)                                               // interface language: English
            .Press(ConsoleKey.Enter)                                                // dictation language: English
            .Press(ConsoleKey.Enter)                                                // system check: install
            .Press(ConsoleKey.Enter)                                                // system check: all there, go on
            .Press(ConsoleKey.Enter)                                                // hardware: automatic
            .Press(ConsoleKey.Enter)                                                // model: the recommended one
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)            // key: keep the default
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)        // test dictation: finish
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        SetupOutcome outcome = await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert: the command ran in the terminal, said what it installs, and the system was looked at again
        Assert.Equal(SetupOutcome.Completed, outcome);
        _shell.Verify(shell => shell.RunInTerminalAsync(command, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("sudo apt-get install -y xsel", _console.Output);
        _requirements.Verify(requirements => requirements.Check(), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_DesktopGivesTheKey_AsksTheDesktopForTheKeyAndForPasting()
    {
        // Arrange: a Wayland desktop gives "Ctrl+`" when asked for the default key, and allows pasting
        _desktop.SetupGet(desktop => desktop.AssignsKeys).Returns(true);
        _desktop.SetupGet(desktop => desktop.AsksToPaste).Returns(true);
        _desktop.SetupGet(desktop => desktop.MayPaste).Returns(false);
        _desktop.Setup(desktop => desktop.AssignKeyAsync(HotkeyChoice.Default, It.IsAny<CancellationToken>())).ReturnsAsync("Ctrl+`");
        _desktop.Setup(desktop => desktop.AskToPasteAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _keys.Press(ConsoleKey.Enter)                                               // interface language: English
            .Press(ConsoleKey.Enter)                                                // dictation language: English
            .Press(ConsoleKey.Enter)                                                // hardware: automatic
            .Press(ConsoleKey.Enter)                                                // model: the recommended one
            .Press(ConsoleKey.Enter)                                                // key: open the desktop's window
            .Press(ConsoleKey.Enter)                                                // pasting: open the desktop's window
            .Press(ConsoleKey.Enter)                                                // pasting: allowed, continue
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered)        // test dictation: finish
            .Press(ConsoleKey.UpArrow).Press(ConsoleKey.Enter)                      // additional settings: continue, round from the first setting
            .Press(ConsoleKey.Enter);                                               // review: save

        // Act
        SetupOutcome outcome = await CreateRunner().RunAsync(onlyNewSteps: true, CancellationToken.None);

        // Assert: the keyboard was never listened to; the key is the desktop's, under the name it gave
        UserSettings saved = CreateStore().Load();

        Assert.Equal(SetupOutcome.Completed, outcome);
        Assert.Equal(0, _hotkeys.StartCount);
        Assert.Equal("Ctrl+`", saved.HotkeyLabel);
        Assert.Contains("Echo may paste the text for you", _console.Output);
        Assert.Contains("step 4 of 6 · Hotkey · Pasting", ScreensShown());
        _desktop.Verify(desktop => desktop.AskToPasteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    ///     Queues the keys that accept everything the wizard suggests up to its last step.
    /// </summary>
    private ScriptedInput ReachAdditionalSettings() =>
        _keys.Press(ConsoleKey.Enter)                                               // interface language: English
            .Press(ConsoleKey.Enter)                                                // dictation language: English
            .Press(ConsoleKey.Enter)                                                // hardware: automatic
            .Press(ConsoleKey.Enter)                                                // model: the recommended one
            .PressWhen(ConsoleKey.Enter, () => _hotkeys.StartCount == 1)            // key: keep the default
            .PressWhen(ConsoleKey.Enter, () => _microphones.IsEveryDeviceHeard)     // input device: the first one
            .Press(ConsoleKey.Enter)                                                // silence level: measure
            .Press(ConsoleKey.Enter)                                                // silence level: measured, continue
            .PressWhen(ConsoleKey.Enter, () => _dictation.IsPhraseDelivered);       // test dictation: finish

    /// <summary>
    ///     The boxes drawn for the screens whose title contains <paramref name="title"/>, in the order
    ///     they appeared, each as its lines from the top of the frame to the bottom of it.
    /// </summary>
    private List<string[]> FramesOf(string title)
    {
        string[] lines = _console.Output.Split('\n');
        var frames = new List<string[]>();

        for (int top = 0; top < lines.Length; top++)
        {
            if (!lines[top].Contains("╔═") || !lines[top].Contains(title))
                continue;

            int bottom = Array.FindIndex(lines, top, line => line.Contains('╚'));

            string[] frame = lines[top..(bottom + 1)];

            if (frames.Count == 0 || !frames[^1].SequenceEqual(frame))
                frames.Add(frame);
        }

        return frames;
    }

    /// <summary>
    ///     The titles of the screens in the order they appeared, read from the frame of every screen drawn.
    /// </summary>
    private List<string> ScreensShown()
    {
        var screens = new List<string>();

        foreach (System.Text.RegularExpressions.Match match in ScreenTitle().Matches(_console.Output))
        {
            string title = match.Groups[1].Value.Trim();

            if (screens.Count == 0 || screens[^1] != title)
                screens.Add(title);
        }

        return screens;
    }

    [GeneratedRegex(@"╔═ Setup · (.+?) ═")]
    private static partial Regex ScreenTitle();

    /// <summary>
    ///     Makes the stand-in report as missing whichever libraries are not in its folder at the moment.
    /// </summary>
    private void TrackLibrariesInFolder() =>
        _cuda.Setup(cuda => cuda.FindMissingLibraries())
            .Returns(() => [.. _libraries.Where(library => !File.Exists(Path.Combine(_cuda.Object.Directory, library)))]);

    private UserSettingsStore CreateStore() =>
        new(new AppPaths(_home, _home));

    private SetupRunner CreateRunner()
    {
        var paths = new AppPaths(_home, _home);
        var timings = new SetupTimings(
            TimeSpan.FromMilliseconds(1), FakeMicrophoneProbe.QuietTime, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5));
        var ui = new SetupUi(new ScriptedConsole(_console, _keys), Theme.Full, timings.Tick);

        var hardwareProbe = new Mock<IHardwareProbe>();
        hardwareProbe.Setup(probe => probe.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _hardware);

        var runtime = new Mock<IRecognitionRuntime>();
        runtime.SetupGet(recognition => recognition.Loaded).Returns(() => _loadedRuntime);

        ISetupStep[] steps =
        [
            new LanguageStep(ui),
            new AccelerationStep(ui, hardwareProbe.Object, _cuda.Object, _shell.Object, _requirements.Object, runtime.Object),
            new ModelStep(ui, paths, _downloader.Object, _shell.Object),
            new HotkeyStep(ui, _hotkeys, _desktop.Object),
            new CheckStep(ui, _microphones, _dictation, timings, _microphones.Clock),
            new AdditionalSettingsStep(ui),
        ];

        return new SetupRunner(steps, ui, new UserSettingsStore(paths), hardwareProbe.Object);
    }

    /// <summary>
    ///     Stores the settings of a setup completed earlier, as the wizard would have saved them.
    /// </summary>
    private void SaveCompletedSetup(AccelerationBackend backend) =>
        CreateStore().Save(new UserSettings
        {
            SetupVersion = SetupRunner.CurrentVersion,
            UiLanguage = "en",
            DictationLanguage = "en",
            HardwareBackend = Acceleration.ToSetting(backend),
            ModelName = "ggml-small.en.bin",
            HotkeyKey = "VcF24",
            HotkeyModifier = string.Empty,
            MicrophoneName = "USB Microphone",
        });

    /// <summary>
    ///     A keyboard that hands out keys in a fixed order. A key meant for a screen that redraws on its own
    ///     is held back until its condition is true, so earlier screens of that kind cannot swallow it.
    /// </summary>
    private sealed class ScriptedInput : IAnsiConsoleInput
    {
        /// <summary>
        ///     How long the wizard may wait for the next key. A script out of step with the wizard ends the test
        ///     with a failure instead of leaving it waiting forever.
        /// </summary>
        private static readonly TimeSpan _patience = TimeSpan.FromSeconds(15);

        private readonly Queue<(ConsoleKey Key, char Character, Func<bool>? IsDue)> _keys = new();
        private readonly Stopwatch _sinceLastKey = Stopwatch.StartNew();
        private int _read;

        /// <summary>
        ///     What the wizard has drawn, shown when it waits too long, so the screen it got stuck on can be seen.
        /// </summary>
        public Func<string> Screen { get; set; } = () => string.Empty;

        public int Remaining => _keys.Count;

        public ScriptedInput Press(ConsoleKey key)
        {
            _keys.Enqueue((key, '\0', null));

            return this;
        }

        public ScriptedInput PressWhen(ConsoleKey key, Func<bool> isDue)
        {
            _keys.Enqueue((key, '\0', isDue));

            return this;
        }

        /// <summary>
        ///     Types the text character by character. The keys carry no key code, the way some terminals
        ///     deliver the keys of a layout other than the English one.
        /// </summary>
        public ScriptedInput Type(string text)
        {
            foreach (char character in text)
                _keys.Enqueue((default, character, null));

            return this;
        }

        public bool IsKeyAvailable()
        {
            if (_keys.TryPeek(out (ConsoleKey Key, char Character, Func<bool>? IsDue) next) && next.IsDue is not null && next.IsDue())
                return true;

            if (_sinceLastKey.Elapsed > _patience)
            {
                string screen = string.Join('\n', Screen().Split('\n').TakeLast(25));

                throw new TimeoutException((_keys.Count == 0
                    ? $"The wizard waited for a key after the {_read} the test provides."
                    : $"Key {_read + 1} ({next.Key}) never became due.") + $" The screen:\n{screen}");
            }

            return false;
        }

        public ConsoleKeyInfo? ReadKey(bool intercept)
        {
            if (_keys.Count == 0)
                throw new InvalidOperationException("The wizard asked for more keys than the test provides.");

            (ConsoleKey key, char character, _) = _keys.Dequeue();

            _read++;
            _sinceLastKey.Restart();

            return new ConsoleKeyInfo(character, key, shift: false, alt: false, control: false);
        }

        public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) =>
            Task.FromResult(ReadKey(intercept));
    }

    private sealed class ScriptedConsole(TestConsole inner, ScriptedInput input) : IAnsiConsole
    {
        public Profile Profile => inner.Profile;

        public IAnsiConsoleCursor Cursor => inner.Cursor;

        public IAnsiConsoleInput Input => input;

        public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;

        public RenderPipeline Pipeline => inner.Pipeline;

        public void Clear(bool home) => inner.Clear(home);

        public void Write(IRenderable renderable) => inner.Write(renderable);

        public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);
    }

    private sealed class FakeHotkeyCaptureFactory : IHotkeyCaptureFactory
    {
        /// <summary>
        ///     The keys "pressed" during each visit of the key screen.
        /// </summary>
        public Queue<HotkeyChoice[]> Presses { get; } = new();

        public int StartCount { get; private set; }

        public IHotkeyCapture Start()
        {
            StartCount++;

            return new Capture(Presses.TryDequeue(out HotkeyChoice[]? presses) ? presses : []);
        }

        private sealed class Capture(HotkeyChoice[] presses) : IHotkeyCapture
        {
            private readonly Queue<HotkeyChoice> _presses = new(presses);

            public bool TryRead(out HotkeyChoice choice) => _presses.TryDequeue(out choice!);

            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    ///     A clock that stands still until it is moved.
    /// </summary>
    private sealed class ReadingClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public void Advance(TimeSpan time) => Interlocked.Add(ref _ticks, time.Ticks);
    }

    /// <summary>
    ///     Microphones that are never late. Their time moves only when they are read, by the same step every time,
    ///     so what the wizard measures depends on how many times it listened, not on how busy the machine is.
    /// </summary>
    private sealed class FakeMicrophoneProbe : IMicrophoneProbe
    {
        public static readonly TimeSpan QuietTime = TimeSpan.FromMilliseconds(120);

        /// <summary>
        ///     How much time one reading of a microphone takes.
        /// </summary>
        private static readonly TimeSpan _readingTime = TimeSpan.FromMilliseconds(10);

        private readonly List<Session> _sessions = [];

        public List<MicrophoneDevice> Devices { get; } = [new(0, "Desk microphone"), new(1, "Headset")];

        /// <summary>
        ///     Devices that hear one level all the time, instead of a quiet room followed by a voice.
        /// </summary>
        public Dictionary<int, float> SteadyLevels { get; } = [];

        /// <summary>
        ///     The clock of the sound step, moved by the readings.
        /// </summary>
        public ReadingClock Clock { get; } = new();

        /// <summary>
        ///     How many times a microphone has been opened.
        /// </summary>
        public int OpenCount
        {
            get
            {
                lock (_sessions)
                    return _sessions.Count;
            }
        }

        /// <summary>
        ///     Whether the list of devices is listening to every one of them.
        /// </summary>
        public bool IsEveryDeviceHeard => IsListeningTo(Devices.Count);

        public IReadOnlyList<MicrophoneDevice> GetDevices() => Devices;

        public IMicrophoneSession Open(int deviceIndex)
        {
            var session = new Session(Clock, SteadyLevels.TryGetValue(deviceIndex, out float level) ? level : null);

            lock (_sessions)
                _sessions.Add(session);

            return session;
        }

        /// <summary>
        ///     Whether exactly this many microphones are open, and every one of them is already being listened to.
        /// </summary>
        public bool IsListeningTo(int count)
        {
            lock (_sessions)
            {
                List<Session> open = [.. _sessions.Where(session => !session.IsDisposed)];

                return open.Count == count && open.All(session => session.IsRead);
            }
        }

        /// <summary>
        ///     A quiet room from the moment it is opened, then a voice. The voice starts a little late, as a real person's would.
        /// </summary>
        private sealed class Session(ReadingClock clock, float? steadyLevel) : IMicrophoneSession
        {
            private readonly long _openedAt = clock.GetTimestamp();

            public bool IsRead { get; private set; }

            public bool IsDisposed { get; private set; }

            public float ReadPeak()
            {
                IsRead = true;
                clock.Advance(_readingTime);

                return steadyLevel ?? (clock.GetElapsedTime(_openedAt) < QuietTime + TimeSpan.FromMilliseconds(40) ? 0.002f : 0.3f);
            }

            public void Dispose() => IsDisposed = true;
        }
    }

    private sealed class FakeDictationTester : IDictationTester
    {
        private Session? _session;

        public int StartCount { get; private set; }

        public bool IsPhraseDelivered => _session?.IsDrained == true;

        public Task<IDictationTestSession> StartAsync(SetupContext draft, CancellationToken cancellationToken)
        {
            StartCount++;
            _session = new Session();

            return Task.FromResult<IDictationTestSession>(_session);
        }

        private sealed class Session : IDictationTestSession
        {
            private readonly Queue<DictationTestEvent> _events = new(
            [
                new DictationTestEvent(DictationTestEventKind.RecordingStarted),
                new DictationTestEvent(DictationTestEventKind.Transcribing),
                new DictationTestEvent(DictationTestEventKind.Recognized, "hello from the test", 420),
            ]);

            public RuntimeLibrary? Runtime => RuntimeLibrary.Cpu;

            public bool IsDrained => _events.Count == 0;

            public bool TryRead(out DictationTestEvent testEvent) => _events.TryDequeue(out testEvent!);

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}