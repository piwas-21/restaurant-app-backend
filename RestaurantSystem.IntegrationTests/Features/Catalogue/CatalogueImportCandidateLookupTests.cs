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
public sealed class CatalogueImportCandidateLookupTests(DatabaseFixture databaseFixture)
    : IntegrationTestBase(databaseFixture)
{
    private const string Actor = "catalogue-candidate-query-test";

    [Fact]
    public async Task Candidate_queries_bound_each_name_in_database_and_prioritize_canonical_names()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var categoryName = $"category-match-{suffix}";
        var secondCategoryName = $"category-second-{suffix}";
        var ingredientName = $"ingredient-match-{suffix}";
        var optionSetName = $"option-set-match-{suffix}";
        var productName = $"product-match-{suffix}";
        var bundleName = $"bundle-match-{suffix}";
        var categoryRows = Enumerable.Range(0, 8).Select(index => new Category
        {
            Id = Guid.NewGuid(),
            Name = categoryName,
            DisplayOrder = index,
            CreatedBy = Actor
        }).ToArray();
        var secondCategoryRows = Enumerable.Range(0, 2).Select(index => new Category
        {
            Id = Guid.NewGuid(),
            Name = secondCategoryName,
            DisplayOrder = index,
            CreatedBy = Actor
        }).ToArray();
        var canonicalIngredient = NewIngredient(ingredientName);
        var translatedIngredients = Enumerable.Range(0, 7)
            .Select(index => NewIngredient($"translated-ingredient-{index}-{suffix}", ingredientName)).ToArray();
        var canonicalOptionSet = NewOptionSet(optionSetName);
        var translatedOptionSets = Enumerable.Range(0, 7)
            .Select(index => NewOptionSet($"translated-option-set-{index}-{suffix}", optionSetName)).ToArray();
        var canonicalProduct = NewProduct(productName);
        var translatedProducts = Enumerable.Range(0, 7)
            .Select(index => NewProduct($"translated-product-{index}-{suffix}", productName)).ToArray();
        var bundleCategory = new Category
        {
            Name = $"bundle-category-{suffix}",
            CreatedBy = Actor
        };
        var bundle = NewProduct($"internal-bundle-{suffix}", bundleName, ProductType.Menu);
        bundle.ProductCategories.Add(new ProductCategory
        {
            Category = bundleCategory,
            DisplayOrder = 0,
            IsPrimary = true,
            CreatedBy = Actor
        });

        await using (var seed = DatabaseFixture.CreateContext())
        {
            seed.Categories.AddRange(categoryRows.Concat(secondCategoryRows).Append(bundleCategory));
            seed.GlobalIngredients.AddRange([canonicalIngredient, .. translatedIngredients]);
            seed.OptionSets.AddRange([canonicalOptionSet, .. translatedOptionSets]);
            seed.Products.AddRange([canonicalProduct, .. translatedProducts, bundle]);
            await seed.SaveChangesAsync();
        }

        var capture = new CandidateQueryCapture();
        await using var context = DatabaseFixture.CreateContext(capture);
        var candidates = await CatalogueImportCandidateLookup.LoadAsync(context,
        [
            new CatalogueCandidateSearch("category", categoryName),
            new CatalogueCandidateSearch("category", secondCategoryName),
            new CatalogueCandidateSearch("ingredient", ingredientName),
            new CatalogueCandidateSearch("option-set", optionSetName),
            new CatalogueCandidateSearch("item", productName),
            new CatalogueCandidateSearch("bundle", bundleName)
        ], CancellationToken.None);

        var firstCategoryMatches = candidates[CatalogueImportCandidateLookup.Key("category", categoryName)];
        firstCategoryMatches.Should().HaveCount(6);
        firstCategoryMatches.Select(candidate => candidate.Id)
            .Should().Equal(categoryRows.OrderBy(row => row.Id).Take(6).Select(row => row.Id));
        candidates[CatalogueImportCandidateLookup.Key("category", secondCategoryName)]
            .Should().HaveCount(2);

        candidates[CatalogueImportCandidateLookup.Key("ingredient", ingredientName)]
            .Should().HaveCount(6).And.Contain(candidate => candidate.Id == canonicalIngredient.Id);
        candidates[CatalogueImportCandidateLookup.Key("option-set", optionSetName)]
            .Should().HaveCount(6).And.Contain(candidate => candidate.Id == canonicalOptionSet.Id);
        candidates[CatalogueImportCandidateLookup.Key("item", productName)]
            .Should().HaveCount(6).And.Contain(candidate => candidate.Id == canonicalProduct.Id);
        var bundleCandidate = candidates[CatalogueImportCandidateLookup.Key("bundle", bundleName)]
            .Should().ContainSingle().Which;
        bundleCandidate.Id.Should().Be(bundle.Id);
        bundleCandidate.CategoryName.Should().Be(bundleCategory.Name);

        capture.Commands.Should().HaveCount(5);
        capture.Commands.Should().OnlyContain(sql =>
            sql.Contains("CROSS JOIN LATERAL", StringComparison.OrdinalIgnoreCase) &&
            sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) &&
            !sql.Contains("SELECT *", StringComparison.OrdinalIgnoreCase));
    }

    private static GlobalIngredient NewIngredient(string defaultName, string? translatedName = null)
    {
        var ingredient = new GlobalIngredient { DefaultName = defaultName, CreatedBy = Actor };
        if (translatedName is not null)
        {
            ingredient.Translations.Add(new GlobalIngredientTranslation
            {
                LanguageCode = "tr",
                Name = translatedName,
                CreatedBy = Actor
            });
        }

        return ingredient;
    }

    private static OptionSet NewOptionSet(string name, string? translatedName = null)
    {
        var optionSet = new OptionSet
        {
            Kind = OptionSetKind.Ingredient,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            CreatedBy = Actor
        };
        if (translatedName is not null)
        {
            optionSet.Translations.Add(new OptionSetTranslation
            {
                LanguageCode = "tr",
                Name = translatedName,
                CreatedBy = Actor
            });
        }

        return optionSet;
    }

    private static Product NewProduct(string name, string? translatedName = null, ProductType type = ProductType.MainItem)
    {
        var product = new Product { Name = name, Type = type, CreatedBy = Actor };
        if (translatedName is not null)
        {
            product.Descriptions.Add(new ProductDescription
            {
                Name = translatedName,
                Description = string.Empty,
                Lang = "tr",
                CreatedBy = Actor
            });
        }

        return product;
    }

    private sealed class CandidateQueryCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
