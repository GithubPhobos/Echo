namespace Echo.Platform;

/// <summary>
///     Windows brings everything Echo needs.
/// </summary>
internal sealed class NoSystemRequirements : ISystemRequirements
{
    /// <inheritdoc/>
    public IReadOnlyList<SystemRequirement> Check() => [];
}