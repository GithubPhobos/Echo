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
    private readonly ISystemRequirements _system;
    private readonly IRecognitionRuntime _runtime;

    /// <summary>
    ///     What the system lacks, found when the wizard first gets here and again whenever the user asks.
    /// </summary>
    private IReadOnlyList<SystemRequirement>? _requirements;

    /// <param name="ui"><see cref="SetupUi"/></param>
    /// <param name="hardwareProbe"><see cref="IHardwareProbe"/></param>
    /// <param name="cudaRuntime"><see cref="ICudaRuntime"/></param>
    /// <param name="shell"><see cref="ISystemShell"/></param>
    /// <param name="system">What Echo needs from this system besides itself.</param>
    /// <param name="runtime"><see cref="IRecognitionRuntime"/></param>
    public AccelerationStep(SetupUi ui,
                            IHardwareProbe hardwareProbe,
                            ICudaRuntime cudaRuntime,
                            ISystemShell shell,
                            ISystemRequirements system,
                            IRecognitionRuntime runtime)
    {
        _ui = ui;
        _hardwareProbe = hardwareProbe;
        _cudaRuntime = cudaRuntime;
        _shell = shell;
        _system = system;
        _runtime = runtime;

        Pages =
        [
            // Shown only on a processor recognition cannot run on: setup goes no further, nothing is downloaded
            new SetupPage(
                ExplainProcessorAsync,
                IsNeeded: _ => ProcessorSupport.MissingInstructions().Count > 0),

            // Shown only when the system lacks something Echo needs: before anything is chosen or downloaded
            new SetupPage(
                ShowRequirementsAsync,
                IsNeeded: _ => Requirements().Any(requirement => !requirement.IsMet),
                SubTitle: () => Strings.SubSystemCheck),

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

    private IReadOnlyList<SystemRequirement> Requirements() =>
        _requirements ??= _system.Check();

    /// <summary>
    ///     Lists what the system has and lacks, with how to get what is missing. The user may install it and
    ///     look again, or go on without it.
    /// </summary>
    private async Task<SetupStepResult> ShowRequirementsAsync(SetupContext context,
                                                              SetupScreen screen,
                                                              CancellationToken cancellationToken)
    {
        while (true)
        {
            List<string> lines = [SetupUi.Question(Markup.Escape(Strings.SystemCheckQuestion)), string.Empty];

            foreach (SystemRequirement requirement in Requirements())
            {
                if (requirement.IsMet)
                {
                    lines.Add(_ui.Good(Markup.Escape(requirement.Name)));

                    continue;
                }

                lines.Add(_ui.Bad(Markup.Escape(requirement.Name)));
                lines.Add(SetupUi.Dim($"  {Markup.Escape(requirement.Consequence)}"));

                if (requirement.Fix is not null)
                    lines.Add($"  {SetupUi.Accent(Markup.Escape(requirement.Fix))}");
            }

            // What Echo can install itself, all with one command; what it cannot (the desktop's own parts) is
            // for the user to sort out
            string[] packages = [.. Requirements().Where(requirement => !requirement.IsMet && requirement.Package is not null).Select(requirement => requirement.Package!).Distinct()];
            IReadOnlyList<string>? install = packages.Length > 0 ? _system.InstallCommand(packages) : null;

            lines.AddRange([string.Empty, SetupUi.Small(Markup.Escape(install is null ? Strings.SystemCheckNote : Strings.SystemCheckInstallNote))]);

            List<(ConsoleKey Key, string Hint)> keys = install is null
                ? [(ConsoleKey.Enter, Strings.HintContinue), (ConsoleKey.R, Strings.HintCheckAgain)]
                : [(ConsoleKey.Enter, Strings.HintInstall), (ConsoleKey.R, Strings.HintCheckAgain), (ConsoleKey.S, Strings.HintSkip)];

            UiResult<ConsoleKey> key = await _ui.ShowAsync(screen, lines, keys, cancellationToken);

            if (key.IsBack)
                return SetupStepResult.Back;

            if (key.Value == ConsoleKey.S || (key.Value == ConsoleKey.Enter && install is null))
                return SetupStepResult.Next;

            if (key.Value == ConsoleKey.Enter && install is not null)
            {
                await _ui.HandOverAsync(
                    SetupUi.Fill(Strings.SystemInstalling, string.Join(' ', install)),
                    () => _shell.RunInTerminalAsync(install, cancellationToken));
            }

            _requirements = await _ui.WaitAsync(screen, Markup.Escape(Strings.SystemChecking), () => Task.Run(_system.Check), cancellationToken);
        }
    }

    /// <summary>
    ///     Says that this processor cannot run recognition. There is no way forward from here, only back.
    /// </summary>
    private async Task<SetupStepResult> ExplainProcessorAsync(SetupContext context,
                                                              SetupScreen screen,
                                                              CancellationToken cancellationToken)
    {
        await _ui.ShowAsync(
            screen,
            [
                _ui.Bad(Markup.Escape(Strings.Format(nameof(Strings.ProcessorUnsupported), string.Join(", ", ProcessorSupport.MissingInstructions())))),
                string.Empty,
                SetupUi.Dim(Markup.Escape(Strings.ProcessorUnsupportedHint)),
            ],
            [],
            cancellationToken);

        return SetupStepResult.Back;
    }

    private async Task<SetupStepResult> ChooseBackendAsync(SetupContext context,
                                                           SetupScreen screen,
                                                           CancellationToken cancellationToken)
    {
        if (context.Hardware is null)
        {
            context.UseDetected(await _ui.WaitAsync(
                screen,
                Markup.Escape(Strings.HardwareChecking),
                () => _hardwareProbe.DetectAsync(cancellationToken),
                cancellationToken));
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

            // On Linux the library is often a link to a file with the full version in its name,
            // such as 'libcublas.so.13.8.0.4' for 'libcublas.so.13'; either one will do
            string? library = _cudaRuntime.Libraries.FirstOrDefault(
                candidate => string.Equals(candidate, name, PathNames.Comparison)
                    || name.StartsWith(candidate + ".", PathNames.Comparison));

            if (library is null)
            {
                await ReportAsync(screen, Strings.Format(nameof(Strings.CudaNotALibrary), name), details: null, cancellationToken);

                continue;
            }

            string target = Path.Combine(_cudaRuntime.Directory, library);

            // Picked from Echo's own folder: it is already where it belongs
            if (PathNames.AreSame(source, target))
                continue;

            long size = new FileInfo(source).Length;
            long free = _shell.GetFreeSpace(_cudaRuntime.Directory);

            UiResult<Exception?> outcome = free < size
                ? UiResult<Exception?>.Done(new IOException(Strings.Format(
                    nameof(Strings.DiskSpaceLow), Format.Megabytes(size), Format.Megabytes(free))))
                : await _ui.CopyAsync(
                    screen,
                    library,
                    size,
                    (progress, token) => FileCopier.CopyAsync(source, target, progress, token),
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

        // Found out before hundreds of megabytes come down, not after
        if (_cudaRuntime.MissingTools() is { Count: > 0 } tools)
        {
            await ReportAsync(screen, Strings.Format(nameof(Strings.CudaToolsMissing), string.Join(", ", tools)), details: null, cancellationToken);

            return;
        }

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

        // Where Vulkan is still on trial it is there to pick by hand, and says so
        if (hardware.IsVulkanAvailable)
        {
            choices.Add(hardware.IsVulkanChosenAutomatically
                ? new(Acceleration.ToSetting(AccelerationBackend.Vulkan), "Vulkan", Hint: Strings.AccelerationVulkanHint)
                : new(Acceleration.ToSetting(AccelerationBackend.Vulkan), Strings.AccelerationVulkanTrial, Hint: Strings.AccelerationVulkanTrialHint));
        }

        choices.Add(new(Acceleration.ToSetting(AccelerationBackend.Cpu), Strings.AccelerationCpu, Hint: Strings.AccelerationCpuHint));

        // A backend the loaded runtime cannot switch to says so while it is highlighted, before it is picked
        return
        [
            .. choices.Select(choice => NeedsRestart(choice.Value, hardware)
                ? choice with { Caution = Strings.AccelerationRestartCaution }
                : choice),
        ];
    }

    /// <summary>
    ///     Whether the backend of a row of the list would take effect only in a fresh start of Echo.
    /// </summary>
    private bool NeedsRestart(string setting, HardwareInfo hardware) =>
        Acceleration.NeedsRestart(Acceleration.Parse(setting) ?? Acceleration.Best(hardware), _runtime.Loaded);

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

        // Vulkan, too, says why it is not on the list
        string? vulkan = hardware.Vulkan switch
        {
            VulkanStatus.NoDriver => Strings.VulkanNoDriver,
            VulkanStatus.SoftwareOnly => Strings.VulkanSoftwareOnly,
            _ => null
        };

        if (vulkan is not null)
            notes.Add(SetupUi.Dim(Markup.Escape(vulkan)));

        return notes;
    }

    private static string BackendName(AccelerationBackend backend) => backend switch
    {
        AccelerationBackend.Cuda => "CUDA",
        AccelerationBackend.Vulkan => "Vulkan",
        _ => Strings.AccelerationCpu
    };
}