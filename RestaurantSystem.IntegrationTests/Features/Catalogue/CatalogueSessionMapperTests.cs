using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CatalogueSessionMapperTests
{
    [Fact]
    public void Localized_SourceLocaleUsesCanonicalTextBeforeFallbacks()
    {
        var revision = Revision();

        var localized = CatalogueSessionMapper.Localized(revision, "tr");

        Assert.Equal("Canonical name", localized.Name);
        Assert.Equal("Canonical description", localized.Description);
    }

    [Fact]
    public void Localized_OtherLocaleKeepsExplicitFallbackOrder()
    {
        var revision = Revision();

        var localized = CatalogueSessionMapper.Localized(revision, "nl");

        Assert.Equal("English fallback", localized.Name);
        Assert.Equal("Fallback description", localized.Description);
    }

    private static CentralCatalogueTemplateRevision Revision() => new()
    {
        TemplateId = "dish",
        Revision = 1,
        Type = "item",
        Name = "Canonical name",
        Description = "Canonical description",
        SourceLocale = "tr",
        LocaleFallbacks = ["en"],
        Translations = new Dictionary<string, CentralCatalogueTranslation>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new() { Name = "English fallback", Description = "Fallback description" }
        }
    };
}
