namespace Echo.Setup.Models;

/// <summary>
///     Picks the model to suggest for a machine. The thresholds are a starting point taken from the model
///     requirements in the readme; the test dictation at the end of setup shows whether the pick was right.
///     Nothing below small is ever suggested (see <see cref="ModelCatalog.IsNotRecommended"/>).
/// </summary>
internal static class ModelRecommender
{
    /// <summary>
    ///     Video memory assumed for a graphics card that does not report it (AMD and Intel through Vulkan).
    /// </summary>
    private const int UnknownVideoMemoryMb = 4000;

    public static readonly IReadOnlyList<ModelRule> Rules =
    [
        new(ComputeKind.Gpu, 8000, 0, 0, ModelCatalog.LargeTurbo),
        new(ComputeKind.Gpu, 5000, 0, 0, ModelCatalog.Medium),
        new(ComputeKind.Gpu, 0, 0, 0, ModelCatalog.Small),
        new(ComputeKind.Cpu, 0, 0, 0, ModelCatalog.Small),
    ];

    /// <summary>
    ///     Returns the recommended model out of <paramref name="candidates"/>.
    /// </summary>
    /// <param name="hardware">The detected hardware.</param>
    /// <param name="backend">The backend the model will actually run on.</param>
    /// <param name="translate">Whether speech will be translated into English.</param>
    /// <param name="candidates">The models offered to the user.</param>
    public static ModelInfo Recommend(HardwareInfo hardware,
                                      AccelerationBackend backend,
                                      bool translate,
                                      IReadOnlyList<ModelInfo> candidates)
    {
        ComputeKind kind = backend == AccelerationBackend.Cpu ? ComputeKind.Cpu : ComputeKind.Gpu;
        int videoMemoryMb = hardware.Nvidia?.MemoryMb ?? UnknownVideoMemoryMb;

        string tier = Rules
            .First(rule =>
                rule.Kind == kind
                && (kind == ComputeKind.Cpu || videoMemoryMb >= rule.MinVideoMemoryMb)
                && hardware.MemoryGb >= rule.MinMemoryGb
                && hardware.ProcessorCores >= rule.MinCores)
            .Tier;

        // The turbo model was not trained to translate, so the next best tier is suggested instead
        if (translate && tier == ModelCatalog.LargeTurbo)
            tier = ModelCatalog.Medium;

        return candidates.FirstOrDefault(model => model.Tier == tier) ?? candidates[0];
    }
}