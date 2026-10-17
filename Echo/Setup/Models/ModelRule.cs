namespace Echo.Setup.Models;

/// <summary>
///     One line of the recommendation table: the first rule the machine satisfies wins.
/// </summary>
/// <param name="Kind">Where the model runs.</param>
/// <param name="MinVideoMemoryMb">Required video memory; ignored for the processor.</param>
/// <param name="MinMemoryGb">Required system memory.</param>
/// <param name="MinCores">Required processor cores.</param>
/// <param name="Tier">The recommended model tier.</param>
internal sealed record ModelRule(ComputeKind Kind, int MinVideoMemoryMb, int MinMemoryGb, int MinCores, string Tier);