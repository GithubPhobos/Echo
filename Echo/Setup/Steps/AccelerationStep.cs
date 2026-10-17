using System.Collections.Concurrent;
using Echo.Setup.Downloads;
using Echo.Setup.Hardware;
using Echo.UI;
using Spectre.Console;

namespace Echo.Setup.Steps;

/// <summary>
///     Step 2: what the model runs on. The hardware is detected and the best option is suggested; when that is
///     CUDA, the NVIDIA libraries it needs are downloaded or copied from where the user already has them.
/// </summary>
internal sealed class AccelerationStep : ISetupStep
{
    /// <summary>
    ///     Separates file name patterns handed to the file chooser.
    /// </summary>
    private const char PatternSeparator = ';';

    private enum LibrarySource
    {
        Download,
        AlreadyHave
    }

    private readonly SetupUi _ui;
    private readonly IHardwareProbe _hardwareProbe;
    private readonly ICudaRuntime _cudaRuntime;
    private readonly ISystemShell _shell;

    /// <param name="ui"><see cref="SetupUi"/></param>
    /// <param name="hardwareProbe"><see cref="IHardwareProbe"/></param>
    /// <param name="cudaRuntime"><see cref="ICudaRuntime"/></param>
    /// <param name="shell"><see cref="ISystemShell"/></param>
    public AccelerationStep(SetupUi ui, IHardwareProbe hardwareProbe, ICudaRuntime cudaRuntime, ISystemShell shell)
    {
        _ui = ui;
        _hardwareProbe = hardwareProbe;
        _cudaRuntime = cudaRuntime;
        _shell = shell;

        Pages =
        [
            new SetupPage(ChooseBackendAsync),

            // Asked only when CUDA has more than one card to run on
            new SetupPage(
                ChooseGraphicsCardAsync,
                IsNeeded: HasSeveralCards,
                SubTitle: () => Strings.SubGraphicsCard),

            // Shown only when CUDA was picked and its libraries are nowhere to be found
            new SetupPage(
                GetLibrariesAsync,
                IsNeeded: NeedsLibraries,
                SubTitle: () => Strings.SubCudaLibraries),
        ];
    }

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public string Title => Strings.StepAcceleration;

    /// <inheritdoc/>
    public IReadOnlyList<SetupPage> Pages { get; }

    /// <inheritdoc/>
    public IEnumerable<(string Label, string Value)> Summarize(SetupContext context)
    {
        string backend = BackendName(context.RequestedBackend);

        bool isAutomatic = Acceleration.Parse(context.HardwareBackend) is null;

        yield return (Strings.SummaryAcceleration, isAutomatic ? $"{Strings.AccelerationAuto} ({backend})" : backend);
    }

    /// <inheritdoc/>
    public string? Brief(SetupContext context) => context.Hardware is null
        ? null
        : context.RequestedBackend switch
        {
            AccelerationBackend.Cuda => "CUDA",
            AccelerationBackend.Vulkan => "Vulkan",
            _ => "CPU"
        };

    private static bool HasSeveralCards(SetupContext context) =>
        context.Hardware is { NvidiaCards.Count: > 1 } && context.RequestedBackend == AccelerationBackend.Cuda;

    private bool NeedsLibraries(SetupContext context) =>
        context.RequestedBackend == AccelerationBackend.Cuda && _cudaRuntime.FindMissingLibraries().Count > 0;

    private async Task<SetupStepResult> ChooseBackendAsync(SetupContext context,
                                                           SetupScreen screen,
                                                           CancellationToken cancellationToken)
    {
        if (context.Hardware is null)
        {
            HardwareInfo detected = await _ui.WaitAsync(
                screen,
                Markup.Escape(Strings.HardwareChecking),
                () => _hardwareProbe.DetectAsync(cancellationToken),
                cancellationToken);

            // A card picked in an earlier setup stays picked, as long as it is still there
            NvidiaGpu? remembered = detected.NvidiaCards.FirstOrDefault(card => card.Index == context.GraphicsCardIndex);

            context.Hardware = remembered is null ? detected : detected with { Nvidia = remembered };
        }

        List<Choice<string>> choices = BuildChoices(context.Hardware);

        UiResult<string> picked = await _ui.SelectAsync(
            screen,
            Strings.AccelerationQuestion,
            choices,
            Math.Max(0, choices.FindIndex(choice => choice.Value == context.HardwareBackend)),
            DescribeHardware(context.Hardware),
            cancellationToken);

        if (picked.IsBack)
            return SetupStepResult.Back;

        context.HardwareBackend = picked.Value;

        return SetupStepResult.Next;
    }

    private async Task<SetupStepResult> ChooseGraphicsCardAsync(SetupContext context,
                                                                SetupScreen screen,
                                                                CancellationToken cancellationToken)
    {
        IReadOnlyList<NvidiaGpu> cards = context.Hardware!.NvidiaCards;

        UiResult<NvidiaGpu> picked = await _ui.SelectAsync(
            screen,
            Strings.GraphicsCardQuestion,
            [
                .. cards.Select(card => new Choice<NvidiaGpu>(
                    card,
                    card.Name,
                    Summary: SetupUi.Emphasis(Markup.Escape(Format.VideoMemory(card.MemoryMb))),
                    Hint: Strings.Format(nameof(Strings.GraphicsCardHint), Format.VideoMemory(card.MemoryMb)))),
            ],
            Math.Max(0, cards.ToList().FindIndex(card => card.Index == context.Hardware.Nvidia?.Index)),
            notes: null,
            cancellationToken);

        if (picked.IsBack)
            return SetupStepResult.Back;

        context.Hardware = context.Hardware with { Nvidia = picked.Value };
        context.GraphicsCardIndex = picked.Value.Index;

        return SetupStepResult.Next;
    }

    /// <summary>
    ///     Stays open until all the libraries are in place: downloaded, or copied from where the user has them.
    ///     Whoever would rather do without them goes back and picks another backend.
    /// </summary>
    private async Task<SetupStepResult> GetLibrariesAsync(SetupContext context,
                                                          SetupScreen screen,
                                                          CancellationToken cancellationToken)
    {
        string size = Format.Megabytes(_cudaRuntime.DownloadSizeBytes);

        List<Choice<LibrarySource>> choices =
        [
            new(LibrarySource.Download,
                Strings.CudaDownload,
                Summary: SetupUi.Emphasis(Markup.Escape(size)),
                Hint: Strings.Format(nameof(Strings.CudaDownloadHint), size)),
        ];

        if (_shell.CanPickFiles)
            choices.Add(new(
                LibrarySource.AlreadyHave,
                Strings.CudaAlreadyHave,
                Summary: Markup.Escape(Strings.CudaAlreadyHaveSummary),
                Hint: Strings.CudaAlreadyHaveHint));

        LibrarySource selected = LibrarySource.Download;

        while (true)
        {
            IReadOnlyList<string> missing = _cudaRuntime.FindMissingLibraries();

            if (missing.Count == 0)
                return SetupStepResult.Next;

            UiResult<LibrarySource> source = await _ui.SelectAsync(
                screen,
                Strings.CudaLibrariesQuestion,
                choices,
                choices.FindIndex(choice => choice.Value == selected),
                [
                    string.Empty,
                    .. DescribeLibraries(library => !missing.Contains(library)),
                    string.Empty,
                    SetupUi.Question(Markup.Escape(Strings.CudaSourceQuestion)),
                ],
                cancellationToken);

            if (source.IsBack)
                return SetupStepResult.Back;

            selected = source.Value;

            if (selected == LibrarySource.AlreadyHave)
                await ImportAsync(screen, cancellationToken);
            else
                await DownloadAsync(screen, size, missing, cancellationToken);
        }
    }

    /// <summary>
    ///     Lets the user point at the libraries in the file chooser and copies them into Echo's own folder.
    /// </summary>
    private async Task ImportAsync(SetupScreen screen, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> files = await _shell.PickFilesAsync(
            Strings.CudaPickFilesTitle,
            string.Join(PatternSeparator, _cudaRuntime.Libraries),
            cancellationToken);

        foreach (string source in files)
        {
            string name = Path.GetFileName(source);

            string? library = _cudaRuntime.Libraries.FirstOrDefault(
                candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));

            if (library is null)
            {
                await ReportAsync(screen, Strings.Format(nameof(Strings.CudaNotALibrary), name), details: null, cancellationToken);

                continue;
            }

            string target = Path.Combine(_cudaRuntime.Directory, library);

            // Picked from Echo's own folder: it is already where it belongs
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                continue;

            long size = new FileInfo(source).Length;
            long free = _shell.GetFreeSpace(_cudaRuntime.Directory);

            UiResult<Exception?> outcome = free < size
                ? UiResult<Exception?>.Done(new IOException(Strings.Format(
                    nameof(Strings.DiskSpaceLow), Format.Megabytes(size), Format.Megabytes(free))))
                : await _ui.ProgressAsync(
                    screen,
                    () => [$"{SetupUi.Fill(Strings.FileCopying, library)}  {SetupUi.Emphasis(Markup.Escape(Format.Megabytes(size)))}"],
                    (progress, token) => FileCopier.CopyAsync(source, target, progress, token),
                    Strings.CopyPaused,
                    cancellationToken);

            if (outcome.IsBack)
                return;

            if (outcome.Value is not null)
                await ReportAsync(screen, Strings.FileCopyFailed, outcome.Value.Message, cancellationToken);
        }
    }

    /// <summary>
    ///     Downloads the libraries. Whether they are all in place afterwards is for the caller to see.
    /// </summary>
    private async Task DownloadAsync(SetupScreen screen, string size, IReadOnlyList<string> missing, CancellationToken cancellationToken)
    {
        // The libraries that are here already, and those the download has brought so far
        var ready = new ConcurrentDictionary<string, bool>(
            _cudaRuntime.Libraries.Except(missing).Select(library => KeyValuePair.Create(library, true)));

        var downloaded = new InlineProgress<string>(library => ready[library] = true);

        while (true)
        {
            long free = _shell.GetFreeSpace(_cudaRuntime.Directory);
            long required = _cudaRuntime.RequiredSpaceBytes();

            UiResult<Exception?> outcome = free < required
                ? UiResult<Exception?>.Done(new DownloadException(Strings.Format(
                    nameof(Strings.DiskSpaceLow), Format.Megabytes(required), Format.Megabytes(free))))
                : await _ui.ProgressAsync(
                    screen,
                    () =>
                    [
                        $"{Markup.Escape(Strings.CudaDownloading)}  {SetupUi.Emphasis(Markup.Escape(size))}",
                        string.Empty,
                        .. DescribeLibraries(ready.ContainsKey),
                    ],
                    (progress, token) => _cudaRuntime.InstallAsync(progress, downloaded, token),
                    Strings.DownloadPaused,
                    cancellationToken);

            if (outcome.IsBack || outcome.Value is null)
                return;

            UiResult<ConsoleKey> retry = await _ui.ShowAsync(
                screen,
                [_ui.Bad(Markup.Escape(Strings.DownloadFailed)), string.Empty, SetupUi.Dim(Markup.Escape(outcome.Value.Message))],
                [(ConsoleKey.Enter, Strings.HintTryAgain)],
                cancellationToken);

            if (retry.IsBack)
                return;
        }
    }

    /// <summary>
    ///     The required libraries, one per line, each marked as present or not.
    /// </summary>
    private IEnumerable<string> DescribeLibraries(Func<string, bool> isPresent) =>
        _cudaRuntime.Libraries.Select(library => isPresent(library)
            ? _ui.Good(Markup.Escape(library))
            : _ui.Bad(Markup.Escape(library)));

    private Task ReportAsync(SetupScreen screen, string message, string? details, CancellationToken cancellationToken)
    {
        List<string> lines = [_ui.Bad(Markup.Escape(message))];

        if (details is not null)
            lines.AddRange([string.Empty, SetupUi.Dim(Markup.Escape(details))]);

        return _ui.ShowAsync(screen, lines, [(ConsoleKey.Enter, Strings.HintContinue)], cancellationToken);
    }

    private List<Choice<string>> BuildChoices(HardwareInfo hardware)
    {
        // What "automatic" resolves to is named in its explanation under the list
        List<Choice<string>> choices =
        [
            new(Acceleration.Auto,
                Strings.AccelerationAuto,
                Hint: Strings.Format(nameof(Strings.AccelerationAutoHint), BackendName(Acceleration.Best(hardware)))),
        ];

        if (CudaRequirements.Check(hardware) == CudaSupport.Supported)
        {
            choices.Add(new(
                Acceleration.ToSetting(AccelerationBackend.Cuda),
                "CUDA",
                Hint: Strings.Format(
                    nameof(Strings.AccelerationCudaHint),
                    CudaRequirements.MinimumDriver.Major,
                    Format.Megabytes(_cudaRuntime.DownloadSizeBytes))));
        }

        if (hardware.IsVulkanAvailable)
            choices.Add(new(Acceleration.ToSetting(AccelerationBackend.Vulkan), "Vulkan", Hint: Strings.AccelerationVulkanHint));

        choices.Add(new(Acceleration.ToSetting(AccelerationBackend.Cpu), Strings.AccelerationCpu, Hint: Strings.AccelerationCpuHint));

        return choices;
    }

    private static List<string> DescribeHardware(HardwareInfo hardware)
    {
        var notes = new List<string>();

        if (hardware.Nvidia is null)
        {
            notes.Add(SetupUi.Dim(Markup.Escape(Strings.HardwareNoNvidia)));
        }
        else if (hardware.NvidiaCards.Count > 1)
        {
            notes.Add(SetupUi.Dim(SetupUi.Fill(Strings.HardwareGpus, hardware.Nvidia.DriverVersion.ToString())));

            foreach (NvidiaGpu card in hardware.NvidiaCards)
                notes.Add(SetupUi.Dim($"  {SetupUi.Fill("{0}, {1}", card.Name, Format.VideoMemory(card.MemoryMb))}"));
        }
        else
        {
            // The card and its driver stand out: they are what the list below was built from
            notes.Add(SetupUi.Dim(SetupUi.Fill(
                Strings.HardwareGpu,
                hardware.Nvidia.Name,
                Format.VideoMemory(hardware.Nvidia.MemoryMb),
                hardware.Nvidia.DriverVersion.ToString())));
        }

        // Says why the fastest option is not on the list, so nobody has to guess
        string? reason = CudaRequirements.Check(hardware) switch
        {
            CudaSupport.GpuTooOld => Strings.CudaGpuTooOld,
            CudaSupport.DriverTooOld => Strings.Format(nameof(Strings.CudaDriverTooOld), CudaRequirements.MinimumDriver.Major),
            _ => null
        };

        if (reason is not null)
            notes.Add(SetupUi.Dim(Markup.Escape(reason)));

        return notes;
    }

    private static string BackendName(AccelerationBackend backend) => backend switch
    {
        AccelerationBackend.Cuda => "CUDA",
        AccelerationBackend.Vulkan => "Vulkan",
        _ => Strings.AccelerationCpu
    };
}