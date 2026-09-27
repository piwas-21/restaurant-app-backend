using System.Text.Json;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CatalogueImportPayloadReaderTests
{
    [Fact]
    public void EnsureSupportedPayload_AcceptsOptionalIngredientSetWithNonbindingMaximum()
    {
        var revision = Revision("option-set", """
            {
              "kind": "ingredient",
              "min": 0,
              "max": 2,
              "options": [
                { "templateId": "olive", "revision": 1, "sortOrder": 0 },
                { "templateId": "onion", "revision": 1, "sortOrder": 1 }
              ]
            }
            """);

        CatalogueImportPayloadReader.EnsureSupportedPayload(revision);

        var parsed = CatalogueImportPayloadReader.ReadOptionSet(revision);
        Assert.Equal("ingredient", parsed.Kind);
        Assert.Equal(0, parsed.Minimum);
        Assert.Equal(2, parsed.Maximum);
        Assert.Equal(["olive@1", "onion@1"], parsed.Options.Select(option => option.Reference.Key));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(0, 1)]
    public void AddChoiceReviewBlockers_RejectsIngredientGroupCardinality(int minimum, int maximum)
    {
        var revision = Revision("option-set", $$"""
            {
              "kind": "ingredient",
              "min": {{minimum}},
              "max": {{maximum}},
              "options": [
                { "templateId": "olive", "revision": 1, "sortOrder": 0 },
                { "templateId": "onion", "revision": 1, "sortOrder": 1 }
              ]
            }
            """);
        var blockers = new List<CatalogueImportIssueDto>();

        CatalogueImportReviewRules.AddChoiceReviewBlockers(
            revision,
            new CatalogueImportItemDecision { Resolution = "Create" },
            blockers);

        Assert.Contains(blockers, blocker => blocker.Code == "UNSUPPORTED_INGREDIENT_CARDINALITY");
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(0, 1)]
    public void EnsureSupportedPayload_RejectsSuggestedSideBoundsNotRepresentedByTenantContract(
        int minimum,
        int maximum)
    {
        var revision = Revision("option-set", $$"""
            {
              "kind": "suggested-side",
              "min": {{minimum}},
              "max": {{maximum}},
              "options": [
                { "templateId": "fries", "revision": 1, "sortOrder": 0 },
                { "templateId": "salad", "revision": 1, "sortOrder": 1 }
              ]
            }
            """);

        var exception = Assert.Throws<BadRequestException>(() =>
            CatalogueImportPayloadReader.EnsureSupportedPayload(revision));

        Assert.Equal("UNSUPPORTED_SUGGESTED_SIDE_CARDINALITY", exception.ErrorCode);
    }

    [Fact]
    public void EnsureSupportedPayload_RejectsVariationReferencesWithStableCode()
    {
        var revision = Revision("item", """
            {
              "category": { "templateId": "mains", "revision": 1 },
              "suggestedIngredients": [],
              "optionSets": [],
              "sideSets": [],
              "variations": [{ "templateId": "size", "revision": 1 }],
              "requiredLocalReviewFields": ["price"]
            }
            """);

        var exception = Assert.Throws<BadRequestException>(() =>
            CatalogueImportPayloadReader.EnsureSupportedPayload(revision));

        Assert.Equal("UNSUPPORTED_VARIATION_REFERENCE", exception.ErrorCode);
    }

    [Fact]
    public void EnsureSupportedPayload_RejectsNonBooleanOptionDefault()
    {
        var revision = Revision("option-set", """
            {
              "kind": "sauce",
              "min": 0,
              "max": 1,
              "options": [{ "templateId": "sauce", "revision": 1, "sortOrder": 0, "default": "yes" }]
            }
            """);

        var exception = Assert.Throws<BadRequestException>(() =>
            CatalogueImportPayloadReader.EnsureSupportedPayload(revision));

        Assert.Equal("UNSUPPORTED_OPTION_DEFAULT", exception.ErrorCode);
    }

    [Fact]
    public void EnsureSupportedPayload_RejectsDuplicateOptionSortOrders()
    {
        var revision = Revision("option-set", """
            {
              "kind": "sauce",
              "min": 0,
              "max": 1,
              "options": [
                { "templateId": "sauce-one", "revision": 1, "sortOrder": 0 },
                { "templateId": "sauce-two", "revision": 1, "sortOrder": 0 }
              ]
            }
            """);

        var exception = Assert.Throws<BadRequestException>(() =>
            CatalogueImportPayloadReader.EnsureSupportedPayload(revision));

        Assert.Equal("UNSUPPORTED_OPTION_ORDERING", exception.ErrorCode);
    }

    [Fact]
    public void EnsureSupportedPayload_RejectsUnderspecifiedOfferFamily()
    {
        var revision = Revision("bundle", """
            {
              "offerFamily": { "templateId": "family", "revision": 1 },
              "sections": [{
                "sectionKey": "main",
                "name": "Main",
                "sortOrder": 0,
                "min": 0,
                "max": 1,
                "options": [{ "templateId": "item", "revision": 1, "sortOrder": 0 }]
              }],
              "requiredLocalReviewFields": ["price"]
            }
            """);

        var exception = Assert.Throws<BadRequestException>(() =>
            CatalogueImportPayloadReader.EnsureSupportedPayload(revision));

        Assert.Equal("UNSUPPORTED_OFFER_FAMILY", exception.ErrorCode);
    }

    [Fact]
    public void ReadBundleSections_PreservesReviewedLocaleNamesAndStableOrder()
    {
        var revision = Revision("bundle", """
            {
              "sections": [{
                "sectionKey": "drinks",
                "name": "Choose a drink",
                "translations": { "tr": { "name": "İçecek seç" }, "fr": { "name": "Choisir une boisson" } },
                "sortOrder": 1,
                "min": 0,
                "max": 1,
                "options": [{ "templateId": "ayran", "revision": 1, "sortOrder": 0 }]
              }],
              "requiredLocalReviewFields": ["price"]
            }
            """);

        var section = Assert.Single(CatalogueImportPayloadReader.ReadBundleSections(revision));

        Assert.Equal("drinks", section.Key);
        Assert.Equal("İçecek seç", section.Translations["tr"]);
        Assert.Equal("Choisir une boisson", section.Translations["fr"]);
    }

    private static CentralCatalogueTemplateRevision Revision(string type, string payloadJson)
    {
        using var payload = JsonDocument.Parse(payloadJson);
        return new CentralCatalogueTemplateRevision
        {
            Type = type,
            Payload = payload.RootElement.Clone()
        };
    }
}
