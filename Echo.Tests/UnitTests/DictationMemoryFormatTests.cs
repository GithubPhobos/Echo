using Echo.Setup.Models;

namespace Echo.Tests.UnitTests;

public sealed class DictationMemoryFormatTests
{
    [Theory]
    [InlineData("ggml-tiny.bin", "m1")]
    [InlineData("tiny.en", "m2")]
    [InlineData("ggml-large-v3.bin", "m10")]
    [InlineData("ggml-large-v3-turbo", "m9")]
    [InlineData("custom-q5.bin", "custom-q5.bin")]
    public void ModelCode_KnownOrCustomModel_IsStableAndLossless(string model, string code) =>
        Assert.Equal(code, DictationMemoryFormat.ModelCode(model));

    [Fact]
    public void Models_WholeCatalog_HasUniqueExplicitCodes()
    {
        Assert.All(ModelCatalog.All, model => Assert.StartsWith("m", DictationMemoryFormat.ModelCode(model.FileName)));
        Assert.Equal(DictationMemoryFormat.Models.Count, DictationMemoryFormat.Models.Values.Distinct().Count());
    }

    [Fact]
    public void Settings_WholeOptionsType_HasOneCodeOrADedicatedTopLevelField()
    {
        string[] topLevel = [nameof(WhisperSettings.ModelName), nameof(WhisperSettings.Language), nameof(WhisperSettings.Translate), nameof(WhisperSettings.HardwareBackend)];
        Assert.All(typeof(WhisperSettings).GetProperties(), property =>
            Assert.True(topLevel.Contains(property.Name) || DictationMemoryFormat.Settings.ContainsKey(property.Name), property.Name));
        Assert.Equal(DictationMemoryFormat.Settings.Count, DictationMemoryFormat.Settings.Values.Distinct().Count());
        Assert.All(typeof(WhisperSettings).GetProperties().Where(p => p.PropertyType == typeof(bool) && p.Name != nameof(WhisperSettings.Translate)),
            property => Assert.StartsWith("e", DictationMemoryFormat.Settings[property.Name]));
    }

    [Fact]
    public void CustomSettings_EffectiveDefaults_AreNotRepeated()
    {
        WhisperSettings defaults = DictationMemoryFormat.Defaults();
        Assert.Empty(DictationMemoryFormat.CustomSettings(defaults, defaults));
        Assert.Equal(new Dictionary<string, object?> { ["t"] = 0.2f, ["enc"] = true },
            DictationMemoryFormat.CustomSettings(new WhisperSettings { Temperature = 0.2f, NoContext = true }, new WhisperSettings()));
    }

    [Fact]
    public void CustomSettings_ExplicitNullDifferentFromBundledValue_IsKept()
    {
        var changes = DictationMemoryFormat.CustomSettings(new WhisperSettings(), new WhisperSettings { Prompt = "default vocabulary" });
        Assert.True(changes.ContainsKey("p"));
        Assert.Null(changes["p"]);
    }
}