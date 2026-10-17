using Echo.Setup;
using Microsoft.Extensions.Configuration;

namespace Echo.Tests.UnitTests;

public sealed class AdditionalSettingsTests
{
    [Fact]
    public void Defaults_AreTheValuesTheProgramShipsWith()
    {
        // Arrange: the settings file that is built into the program
        IConfiguration shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build();

        // Act & Assert: a default that drifted away would show the user a value the program does not use
        Assert.All(AdditionalSettings.All, setting =>
        {
            string? value = shipped[$"{setting.Section}:{setting.Key}"];

            string? expected = string.IsNullOrEmpty(value)
                ? null
                : setting.Kind == SettingKind.Switch ? value.ToLowerInvariant() : value;

            Assert.Equal(expected, setting.Default);
        });
    }

    [Fact]
    public void EverySetting_HasANameAndAnExplanation()
    {
        // Act & Assert: a text that is missing comes back as its own key
        Assert.All(AdditionalSettings.All, setting =>
        {
            Assert.DoesNotContain("Additional", setting.Label);
            Assert.DoesNotContain("Additional", setting.Hint);
            Assert.DoesNotContain("Additional", setting.Caution ?? string.Empty);

            // An explanation is a sentence, and ends like one
            Assert.EndsWith(".", setting.Hint);
        });

        Assert.Equal(AdditionalSettings.All.Count, AdditionalSettings.All.Select(setting => setting.Id).Distinct().Count());
    }
}