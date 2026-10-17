using Echo.Setup.Models;

namespace Echo.Tests.UnitTests;

public sealed class ModelCatalogTests
{
    [Fact]
    public void All_HasSizeAndChecksumForEveryModel()
    {
        // Assert
        Assert.All(ModelCatalog.All, model =>
        {
            Assert.True(model.SizeBytes > 50_000_000);
            Assert.Matches("^[0-9a-f]{64}$", model.Sha256);
            Assert.EndsWith(".bin", model.FileName);
        });

        Assert.Equal(ModelCatalog.All.Count, ModelCatalog.All.Select(model => model.Id).Distinct().Count());
    }

    [Fact]
    public void For_EnglishOnly_PrefersEnglishModelsWhereTheyExist()
    {
        // Act
        string[] ids = [.. ModelCatalog.For(englishOnly: true).Select(model => model.Id)];

        // Assert
        Assert.Equal(["tiny.en", "base.en", "small.en", "medium.en", "large-v3-turbo", "large-v3"], ids);
    }
}