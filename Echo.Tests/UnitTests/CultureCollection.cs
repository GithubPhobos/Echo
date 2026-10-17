namespace Echo.Tests.UnitTests;

/// <summary>
///     Tests that switch the interface language must not run alongside tests that read translated texts.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CultureCollection
{
    public const string Name = "Interface language";
}