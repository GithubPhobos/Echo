using System.Globalization;
using Echo.Setup.Steps;

namespace Echo.Tests.UnitTests;

public sealed class LanguagesTests
{
    [Theory]
    [InlineData("ru-RU", "ru")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("de-AT", "de")]
    [InlineData("fr-FR", "en")]
    public void MatchInterface_PicksTheClosestTranslation(string system, string expected)
    {
        // Act & Assert
        Assert.Equal(expected, Languages.MatchInterface(CultureInfo.GetCultureInfo(system)));
    }
}