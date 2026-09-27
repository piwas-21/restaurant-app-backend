using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantSystem.Api.Features.Catalogue.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

[Collection("Database Lane 3")]
public sealed class CatalogueRevisionBatchingTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    private const string Actor = "catalogue-revision-batching-test";

    [Fact]
    public async Task Bundle_local_text_loads_all_sections_in_a_single_related_query()
    {
        var (firstProduct, firstDefinition, firstSections) =
            NewBundle("First bundle", ["First section", "Second section"]);
        var (secondProduct, secondDefinition, secondSections) = NewBundle("Second bundle", ["Third section"]);
        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.Products.AddRange(firstProduct, secondProduct);
            seed.MenuDefinitions.AddRange(firstDefinition, secondDefinition);
            await seed.SaveChangesAsync();
        }

        var adoptionId = Guid.NewGuid();
        var roots = new[]
        {
            Root("bundle-one", firstProduct.Id),
            Root("bundle-two", secondProduct.Id)
        };
        var entries = new[]
        {
            Entry("bundle-one", "section-a", firstSections[0].Id),
            Entry("bundle-one", "section-b", firstSections[1].Id),
            Entry("bundle-two", "section-a", secondSections[0].Id)
        };
        foreach (var mapping in roots.Concat(entries))
        {
            mapping.AdoptionId = adoptionId;
        }

        var counter = new ReadCommandCounter();
        await using var context = DatabaseFixture.CreateContext(counter);
        var fields = await new CatalogueRevisionLocalTextReader(context)
            .ReadManyAsync(roots, entries, CancellationToken.None);

        fields[roots[0].Id]["name"].Should().Be("First bundle");
        fields[roots[0].Id]["sections[section-a].name"].Should().Be("First section");
        fields[roots[0].Id]["sections[section-a].translations[tr].name"].Should().Be("Birinci bölüm");
        fields[roots[0].Id]["sections[section-b].name"].Should().Be("Second section");
        fields[roots[1].Id]["sections[section-a].name"].Should().Be("Third section");
        counter.ReadCount.Should().Be(2,
            "both products and every mapped section are loaded in two batched SQL reads, not one query per section");
    }

    private static (Product Product, MenuDefinition Definition, MenuSection[] Sections) NewBundle(
        string productName,
        string[] sectionNames)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = productName,
            BasePrice = 1m,
            Type = ProductType.Menu,
            CreatedBy = Actor
        };
        var definition = new MenuDefinition
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            Product = product,
            CreatedBy = Actor
        };
        var sections = sectionNames.Select((name, index) => new MenuSection
        {
            Id = Guid.NewGuid(),
            MenuDefinitionId = definition.Id,
            MenuDefinition = definition,
            Name = name,
            DisplayOrder = index,
            CreatedBy = Actor,
            Translations =
            [
                new MenuSectionTranslation
                {
                    Id = Guid.NewGuid(),
                    LanguageCode = "tr",
                    Name = index == 0 ? "Birinci bölüm" : $"Bölüm {index + 1}",
                    CreatedBy = Actor
                }
            ]
        }).ToArray();
        foreach (var section in sections)
        {
            foreach (var translation in section.Translations)
            {
                translation.MenuSection = section;
            }

            definition.Sections.Add(section);
        }

        product.MenuDefinition = definition;
        return (product, definition, sections);
    }

    private static CatalogueTemplateAdoption Root(string templateId, Guid productId) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = Guid.NewGuid(),
        SourceTemplateId = templateId,
        SourceRevision = 1,
        LocalEntityType = "MenuBundle",
        LocalEntityId = productId,
        ContentHash = new string('a', 64),
        CreatedBy = Actor
    };

    private static CatalogueTemplateAdoption Entry(string templateId, string sectionKey, Guid sectionId) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = Guid.NewGuid(),
        SourceTemplateId = templateId,
        SourceRevision = 1,
        SourceEntryId = sectionKey,
        LocalEntityType = "MenuSection",
        LocalEntityId = sectionId,
        ContentHash = new string('a', 64),
        CreatedBy = Actor
    };

    private sealed class ReadCommandCounter : DbCommandInterceptor
    {
        private int readCount;

        public int ReadCount => Volatile.Read(ref readCount);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref readCount);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
