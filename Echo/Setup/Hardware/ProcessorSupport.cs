using System.Runtime.Intrinsics.X86;

namespace Echo.Setup.Hardware;

/// <summary>
///     The processor instructions the Whisper runtime is built with. A processor without any of them cannot
///     run recognition at all, and Echo does not ship the slower build made for such processors.
/// </summary>
internal static class ProcessorSupport
{
    /// <summary>
    ///     The function of the processor's own identification that lists its features.
    /// </summary>
    private const int FeatureFunction = 1;

    /// <summary>
    ///     The bit of the third register of <see cref="FeatureFunction"/> that stands for F16C,
    ///     which .NET has no ready check for.
    /// </summary>
    private const int F16cBit = 29;

    /// <summary>
    ///     The names of the required instructions the processor of this machine lacks; empty when it has them all.
    /// </summary>
    public static IReadOnlyList<string> MissingInstructions() => Missing(
        Avx.IsSupported,
        Avx2.IsSupported,
        Fma.IsSupported,
        X86Base.IsSupported && (X86Base.CpuId(FeatureFunction, 0).Ecx & (1 << F16cBit)) != 0);

    /// <summary>
    ///     The names of the instructions among the four that are not there.
    /// </summary>
    internal static IReadOnlyList<string> Missing(bool hasAvx, bool hasAvx2, bool hasFma, bool hasF16c) =>
    [
        .. new (bool IsThere, string Name)[] { (hasAvx, "AVX"), (hasAvx2, "AVX2"), (hasFma, "FMA"), (hasF16c, "F16C") }
            .Where(instruction => !instruction.IsThere)
            .Select(instruction => instruction.Name)
    ];
}