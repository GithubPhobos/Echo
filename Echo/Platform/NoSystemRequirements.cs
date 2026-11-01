namespace Echo.Platform;

/// <summary>
///     Windows brings everything Echo needs.
/// </summary>
internal sealed class NoSystemRequirements : ISystemRequirements
{
    /// <inheritdoc/>
    public IReadOnlyList<SystemRequirement> Check() => [];

    /// <inheritdoc/>
    public IReadOnlyList<string>? InstallCommand(IReadOnlyList<string> packages) => null;
}