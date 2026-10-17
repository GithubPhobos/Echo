using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using Echo.Resources;
using Echo.Setup.Steps;

namespace Echo.Tests.UnitTests;

[Collection(CultureCollection.Name)]
public sealed class StringsTests
{
    [Fact]
    public void EveryText_IsTranslatedIntoEveryInterfaceLanguage()
    {
        // Arrange
        var resources = new ResourceManager("Echo.Resources.Strings", typeof(Strings).Assembly);

        IEnumerable<string> keys = typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(property => property.Name);

        // Act & Assert
        foreach (string key in keys)
        {
            string english = resources.GetString(key, CultureInfo.InvariantCulture)!;

            Assert.False(string.IsNullOrWhiteSpace(english), $"'{key}' has no English text.");

            foreach ((string code, _) in Languages.Interface.Where(language => language.Code != Languages.English))
            {
                // Asked without falling back to English, so a missing translation is noticed
                string? translated = resources
                    .GetResourceSet(CultureInfo.GetCultureInfo(code), createIfNotExists: true, tryParents: false)?
                    .GetString(key);

                Assert.False(string.IsNullOrWhiteSpace(translated), $"'{key}' is not translated into '{code}'.");
                Assert.Equal(Placeholders(english), Placeholders(translated!));

                // Square brackets would be read as console markup
                Assert.DoesNotContain('[', translated!);
                Assert.DoesNotContain(']', translated!);
            }
        }
    }

    [Fact]
    public void Get_FollowsTheInterfaceLanguage()
    {
        // Arrange
        CultureInfo original = CultureInfo.CurrentUICulture;

        try
        {
            // Act
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");
            string russian = Strings.StepModel;

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            string english = Strings.StepModel;

            // Assert
            Assert.Equal("Модель", russian);
            Assert.Equal("Model", english);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private static string Placeholders(string text) =>
        string.Join(',', Regex.Matches(text, @"\{\d\}").Select(match => match.Value).Order());
}