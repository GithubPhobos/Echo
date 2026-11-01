namespace Echo.Tests.UnitTests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v2.0.1", "2.0.1")]
    [InlineData("V2.0.1", "2.0.1")]
    [InlineData("2.0.10", "2.0.10")]
    [InlineData(" v3.1.0 ", "3.1.0")]
    public void Parse_Version_WithOrWithoutTheLetterInFront(string text, string expected)
    {
        // Act
        Version? version = ReleaseVersion.Parse(text);

        // Assert
        Assert.Equal(Version.Parse(expected), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2.0")]
    [InlineData("2.0.1.4")]
    [InlineData("v2.1.0-beta")]
    [InlineData("latest")]
    public void Parse_NotThreeNumbers_IsNoVersion(string? text)
    {
        // Act & Assert
        Assert.Null(ReleaseVersion.Parse(text));
    }

    [Theory]
    [InlineData("2.0.10", "2.0.9", true)]
    [InlineData("v2.1.0", "2.0.99", true)]
    [InlineData("3.0.0", "2.9.9", true)]
    [InlineData("2.0.1", "2.0.1", false)]
    [InlineData("2.0.0", "2.0.1", false)]
    [InlineData("v2.1.0-beta", "2.0.1", false)]
    [InlineData("2.0.2", "unknown", false)]
    public void IsNewer_ComparesNumbersAsNumbers(string candidate, string installed, bool expected)
    {
        // Act & Assert
        Assert.Equal(expected, ReleaseVersion.IsNewer(candidate, installed));
    }
}