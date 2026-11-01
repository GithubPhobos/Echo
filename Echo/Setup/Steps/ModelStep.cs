namespace Echo.Setup.Steps;

/// <summary>
///     Step 3: the Whisper model. The one that suits the machine and the dictation language is suggested;
///     the chosen model is downloaded, unless it is already in the models folder. Models the user already
///     has elsewhere on the computer are picked in the file chooser and copied into that folder, after
///     which they are on the list like any other downloaded model.
/// </summary>
internal sealed class ModelStep : ISetupStep
{
    private const string ModelFilePattern = "*.bin";

    /// <summary>
    ///     Stands for "I already have downloaded models" in the list of models.
    /// </summary>
    private const string OwnFileChoice = "";

    /// <summary>
    ///     Every Whisper model in the ggml format starts with these bytes.
    /// </summary>
    private static readonly byte[] _modelSignature = "lmgg"u8.ToArray();

    private readonly SetupUi _ui;
    private readonly IAppPaths _paths;
    private readonly IFileDownloader _downloader;
    private readonly ISystemShell _shell;

    /// <param name="ui"><see cref="SetupUi"/></param>
    /// <param name="paths"><see cref="IAppPaths"/></param>
    /// <param name="downloader"><see cref="IFileDownloader"/></param>
    /// <param name="shell"><see cref="ISystemShell"/></param>
    public ModelStep(SetupUi ui, IAppPaths paths, IFileDownloader downloader, ISystemShell shell)
    {
        _ui = ui;
        _paths = paths;
        _downloader = downloader;
        _shell = shell;

        Pages = [new SetupPage(ChooseModelAsync)];
    }

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public string Title => Strings.StepModel;

    /// <inheritdoc/>
    public IReadOnlyList<SetupPage> Pages { get; }

    /// <inheritdoc/>
    public IEnumerable<(string Label, string Value)> Summarize(SetupContext context)
    {
        yield return (Strings.SummaryModel, context.ModelFileName ?? string.Empty);
    }

    /// <inheritdoc/>
    public string? Brief(SetupContext context)
    {
        if (context.ModelFileName is null)
            return null;

        // The short name from the catalog; a file the user brought is named as it is
        return ModelCatalog.All.FirstOrDefault(model => model.FileName == context.ModelFileName)?.Id
            ?? Path.GetFileNameWithoutExtension(context.ModelFileName);
    }

    /// <summary>
    ///     Whether the file is a Whisper model in the format the runtime reads.
    /// </summary>
    internal static bool IsModelFile(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);

            byte[] signature = new byte[_modelSignature.Length];

            return stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false) == signature.Length
                && signature.AsSpan().SequenceEqual(_modelSignature);
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<SetupStepResult> ChooseModelAsync(SetupContext context,
                                                         SetupScreen screen,
                                                         CancellationToken cancellationToken)
    {
        // English-only models are better, but only for people who never dictate anything else
        bool englishOnly = context.DictationLanguage == Languages.English && !context.Translate;

        IReadOnlyList<ModelInfo> catalog = ModelCatalog.For(englishOnly);

        ModelInfo? recommended = context.Hardware is null
            ? null
            : ModelRecommender.Recommend(context.Hardware, context.RequestedBackend, context.Translate, catalog);

        string preselected = context.ModelFileName ?? recommended?.FileName ?? catalog[0].FileName;

        while (true)
        {
            List<Choice<string>> choices = BuildChoices(catalog, recommended, FindModelFiles(), context.Translate);

            UiResult<string> picked = await _ui.SelectAsync(
                screen,
                Strings.ModelQuestion,
                choices,
                Math.Max(0, choices.FindIndex(choice => choice.Value == preselected)),
                recommended is null ? null : [SetupUi.Dim(Markup.Escape(Strings.ModelRecommendationNote))],
                cancellationToken);

            if (picked.IsBack)
                return SetupStepResult.Back;

            preselected = picked.Value;

            if (picked.Value == OwnFileChoice)
            {
                // The file just brought in is the one the user most likely wants: it is highlighted
                // on the list, now marked as downloaded
                preselected = await BringOwnModelsAsync(screen, cancellationToken) ?? OwnFileChoice;

                continue;
            }

            ModelInfo? model = catalog.FirstOrDefault(candidate => candidate.FileName == picked.Value);

            // A file the user brought, or a catalog model that is already complete on disk
            if (model is null || IsDownloaded(model) || await DownloadAsync(screen, model, cancellationToken))
            {
                context.ModelFileName = picked.Value;

                return SetupStepResult.Next;
            }
        }
    }

    /// <summary>
    ///     Lets the user pick model files in the file chooser of the operating system and copies them into
    ///     the models folder. Returns the name of the last file that is there afterwards, or null.
    /// </summary>
    private async Task<string?> BringOwnModelsAsync(SetupScreen screen, CancellationToken cancellationToken)
    {
        _shell.CreateDirectory(_paths.ModelsDirectory);

        if (_shell.CanPickFiles)
        {
            IReadOnlyList<string> files = await _shell.PickFilesAsync(Strings.ModelPickFilesTitle, ModelFilePattern, cancellationToken);

            return await ImportAsync(screen, files, cancellationToken);
        }

        // Without a file chooser the user puts the files into the folder by hand
        while (true)
        {
            UiResult<ConsoleKey> key = await _ui.ShowAsync(
                screen,
                [SetupUi.Question(Markup.Escape(Strings.ModelCopyQuestion)), string.Empty, SetupUi.Accent(Markup.Escape(_paths.ModelsDirectory))],
                [(ConsoleKey.Enter, Strings.HintContinue), (ConsoleKey.O, Strings.HintOpenFolder)],
                cancellationToken);

            if (key.IsBack || key.Value == ConsoleKey.Enter)
                return null;

            _shell.OpenFolder(_paths.ModelsDirectory);
        }
    }

    /// <summary>
    ///     Copies the picked files into the models folder. Returns the name of the last file that is there
    ///     afterwards, or null when none of them made it.
    /// </summary>
    private async Task<string?> ImportAsync(SetupScreen screen, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        string? imported = null;

        foreach (string source in files)
        {
            string name = Path.GetFileName(source);
            string target = Path.Combine(_paths.ModelsDirectory, name);

            if (!IsModelFile(source))
            {
                await ReportAsync(screen, Strings.Format(nameof(Strings.ModelNotAModel), name), details: null, cancellationToken);

                continue;
            }

            // Picked from the models folder itself, or brought in before
            if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length)
            {
                imported = name;

                continue;
            }

            long size = new FileInfo(source).Length;
            long free = _shell.GetFreeSpace(_paths.ModelsDirectory);

            UiResult<Exception?> outcome = free < size
                ? UiResult<Exception?>.Done(new IOException(Strings.Format(
                    nameof(Strings.DiskSpaceLow), Format.Megabytes(size), Format.Megabytes(free))))
                : await _ui.CopyAsync(
                    screen,
                    name,
                    size,
                    (progress, token) => FileCopier.CopyAsync(source, target, progress, token),
                    cancellationToken);

            if (outcome.IsBack)
                return imported;

            if (outcome.Value is null)
                imported = name;
            else
                await ReportAsync(screen, Strings.FileCopyFailed, outcome.Value.Message, cancellationToken);
        }

        return imported;
    }

    private Task ReportAsync(SetupScreen screen, string message, string? details, CancellationToken cancellationToken)
    {
        List<string> lines = [_ui.Bad(Markup.Escape(message))];

        if (details is not null)
            lines.AddRange([string.Empty, SetupUi.Dim(Markup.Escape(details))]);

        return _ui.ShowAsync(screen, lines, [(ConsoleKey.Enter, Strings.HintContinue)], cancellationToken);
    }

    private List<Choice<string>> BuildChoices(IReadOnlyList<ModelInfo> catalog,
                                             ModelInfo? recommended,
                                             IReadOnlyList<string> present,
                                             bool translate)
    {
        string dot = $" {_ui.Theme.Dot} ";
        var choices = new List<Choice<string>>();

        foreach (ModelInfo model in catalog)
        {
            // Next to the size, in a column of its own: what sets the model apart
            var remarks = new List<string>();

            if (IsDownloaded(model))
                remarks.Add(SetupUi.Done(Markup.Escape(Strings.ModelDownloaded)));

            if (model == recommended)
                remarks.Add(SetupUi.Recommended(Markup.Escape(Strings.TagRecommended)));

            if (ModelCatalog.IsNotRecommended(model))
                remarks.Add(SetupUi.Warning(Markup.Escape(Strings.TagNotRecommended)));

            string hint = Describe(model, translate) + (model == recommended ? $" {Strings.ModelRecommended}." : string.Empty);

            choices.Add(new(model.FileName, model.Id, Summary: Size(model.SizeBytes), Hint: hint)
            {
                Remark = string.Join(dot, remarks)
            });
        }

        // Anything else the user has brought into the folder can be used as it is
        foreach (string file in present.Where(file => catalog.All(model => model.FileName != file)))
        {
            choices.Add(new(file, file, Summary: Size(new FileInfo(Path.Combine(_paths.ModelsDirectory, file)).Length), Hint: Strings.ModelOwnFileHint)
            {
                Remark = SetupUi.Done(Markup.Escape(Strings.ModelDownloaded))
            });
        }

        choices.Add(new(OwnFileChoice, Strings.ModelAlreadyHave, Hint: Strings.ModelAlreadyHaveHint) { IsSetApart = true });

        return choices;

        static string Size(long bytes) => SetupUi.Emphasis(Markup.Escape(Format.Megabytes(bytes)));
    }

    private static string Describe(ModelInfo model, bool translate) => model.Tier switch
    {
        ModelCatalog.Tiny => Strings.ModelTinyHint,
        ModelCatalog.Base => Strings.ModelBaseHint,
        ModelCatalog.Small => Strings.ModelSmallHint,
        ModelCatalog.Medium => Strings.ModelMediumHint,
        ModelCatalog.LargeTurbo => translate ? Strings.ModelTurboNoTranslateHint : Strings.ModelTurboHint,
        _ => Strings.ModelLargeHint
    };

    private async Task<bool> DownloadAsync(SetupScreen screen, ModelInfo model, CancellationToken cancellationToken)
    {
        string target = Path.Combine(_paths.ModelsDirectory, model.FileName);
        var request = new DownloadRequest(model.DownloadUrl, model.SizeBytes, model.Sha256);

        while (true)
        {
            long free = _shell.GetFreeSpace(_paths.ModelsDirectory);

            // What an earlier attempt brought is on the disk already
            long required = _downloader.RemainingBytes(request, target);

            UiResult<Exception?> outcome = free < required
                ? UiResult<Exception?>.Done(new DownloadException(Strings.Format(
                    nameof(Strings.DiskSpaceLow), Format.Megabytes(required), Format.Megabytes(free))))
                : await _ui.ProgressAsync(
                    screen,
                    () => [$"{SetupUi.Fill(Strings.ModelDownloading, model.FileName)}  {SetupUi.Emphasis(Markup.Escape(Format.Megabytes(model.SizeBytes)))}"],
                    (progress, token) => _downloader.DownloadAsync(request, target, progress, token),
                    cancellationToken);

            if (outcome.IsBack)
                return false;

            if (outcome.Value is null)
                return true;

            UiResult<ConsoleKey> retry = await _ui.ShowAsync(
                screen,
                [_ui.Bad(Markup.Escape(Strings.DownloadFailed)), string.Empty, SetupUi.Dim(Markup.Escape(outcome.Value.Message))],
                [(ConsoleKey.Enter, Strings.HintTryAgain)],
                cancellationToken);

            if (retry.IsBack)
                return false;
        }
    }

    private IReadOnlyList<string> FindModelFiles() =>
        Directory.Exists(_paths.ModelsDirectory)
            ? [.. Directory.EnumerateFiles(_paths.ModelsDirectory, ModelFilePattern).Select(Path.GetFileName).Order()!]
            : [];

    private bool IsDownloaded(ModelInfo model)
    {
        var file = new FileInfo(Path.Combine(_paths.ModelsDirectory, model.FileName));

        // A complete download always has the published size; partial ones live under another name
        return file.Exists && file.Length == model.SizeBytes;
    }
}