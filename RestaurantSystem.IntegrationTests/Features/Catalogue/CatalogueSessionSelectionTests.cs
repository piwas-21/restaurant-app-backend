using System.Text.Json;
using FluentAssertions;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CatalogueSessionSelectionTests
{
    [Fact]
    public void Explicit_pack_selection_keeps_required_dependencies_and_excludes_unselected_offer_closure()
    {
        var session = Session();

        CatalogueSessionSelection.Recompute(session, ["offer-b"]);

        Selected(session).Should().BeEquivalentTo("starter-pack", "main-category", "offer-b", "ingredient-b");
    }

    [Fact]
    public void Missing_cuisine_pack_selection_opens_with_every_offer_included()
    {
        var session = Session();

        CatalogueSessionSelection.Recompute(session, null);

        Selected(session).Should().BeEquivalentTo(
            "starter-pack", "main-category", "offer-a", "offer-b", "ingredient-a", "ingredient-b");
    }

    [Fact]
    public void Selection_cannot_include_a_mandatory_or_unknown_template()
    {
        var session = Session();

        var act = () => CatalogueSessionSelection.Recompute(session, ["main-category"]);

        act.Should().Throw<BadRequestException>();
    }

    private static CatalogueImportSession Session()
    {
        var session = new CatalogueImportSession
        {
            Id = Guid.NewGuid(),
            CreatedBy = "test",
            Templates =
            [
                Template("starter-pack", true, false,
                [
                    Dependency("main-category", null),
                    Dependency("offer-a", true),
                    Dependency("offer-b", false)
                ]),
                Template("main-category", false, false, []),
                Template("offer-a", false, true, [Dependency("ingredient-a", null)]),
                Template("offer-b", false, true, [Dependency("ingredient-b", null)]),
                Template("ingredient-a", false, false, []),
                Template("ingredient-b", false, false, [])
            ]
        };
        return session;
    }

    private static CatalogueImportSessionTemplate Template(
        string templateId,
        bool isRoot,
        bool isSelectable,
        List<CentralCatalogueDependency> dependencies) => new()
        {
            Id = Guid.NewGuid(),
            CreatedBy = "test",
            TemplateId = templateId,
            Revision = 1,
            Type = isRoot ? "cuisine-pack" : "item",
            ContentHash = new string('a', 64),
            RevisionJson = JsonSerializer.Serialize(new CentralCatalogueTemplateRevision
            {
                SchemaVersion = 1,
                TemplateId = templateId,
                Revision = 1,
                Type = isRoot ? "cuisine-pack" : "item",
                Name = templateId,
                SourceLocale = "tr",
                Provenance = JsonSerializer.SerializeToElement(new { source = "test" }),
                QualityStatus = "reviewed",
                CompatibleTenantContractVersions = [1],
                Dependencies = dependencies,
                Payload = JsonSerializer.SerializeToElement(new { })
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            IsRoot = isRoot,
            IsSelectable = isSelectable
        };

    private static CentralCatalogueDependency Dependency(string templateId, bool? includedByDefault) => new()
    {
        TemplateId = templateId,
        Revision = 1,
        Role = includedByDefault.HasValue ? "offer" : "category",
        IncludedByDefault = includedByDefault
    };

    private static string[] Selected(CatalogueImportSession session) => session.Templates
        .Where(item => item.IsSelected)
        .Select(item => item.TemplateId)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();
}
