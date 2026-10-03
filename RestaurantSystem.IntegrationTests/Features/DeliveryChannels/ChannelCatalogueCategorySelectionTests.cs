using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Features.DeliveryChannels.Queries.GetChannelCatalogueCandidatesQuery;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelCatalogueCategorySelectionTests(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    private const string CategoriesPath = "/api/delivery-channels/catalogue/categories/snapshot";
    private const string SelectionPath = "/api/delivery-channels/catalogue/selection-snapshot";
    private const string ChangesPath = "/api/delivery-channels/catalogue/categories/compare-snapshot";

    [Fact]
    public async Task CategoryExpansionIsCompleteAcrossCandidatePagesAndOmitsHiddenBase()
    {
        var category = NewCategory("Category page fixture");
        var secondary = NewCategory("Secondary category fixture");
        var products = Enumerable.Range(0, 51).Select(index => Product(category, $"Paged menu item {index}")).ToList();
        var multiCategory = Product(category, "Primary placement fixture");
        multiCategory.ProductCategories.Add(new()
        {
            Category = secondary,
            CategoryId = secondary.Id,
            IsPrimary = false,
            DisplayOrder = 1,
            CreatedBy = "test"
        });
        products.Add(multiCategory);
        var variantOnly = Product(category, "Variant only menu item");
        variantOnly.HideBaseProduct = true;
        var firstVariation = Variation(variantOnly, "Small");
        var secondVariation = Variation(variantOnly, "Large");
        products.Add(variantOnly);
        await Save(category, products, secondary);
        await AuthenticateForCatalogue();

        var categories = await ReadCategories();
        var summary = categories.Categories.Single(row => row.CategoryId == category.Id);
        summary.TotalItemCount.Should().Be(54);
        summary.SupportedItemCount.Should().Be(54);
        categories.CategoryBasis.Should().Be("primaryCategory");
        categories.MaximumCategoryCount.Should().Be(1_000);
        categories.MaximumItemOverrideCount.Should().Be(2_000);
        categories.Categories.Single(row => row.CategoryId == secondary.Id).TotalItemCount.Should().Be(0);
        categories.Revision.Should().MatchRegex("^[a-f0-9]{64}$");

        var firstPage = await Candidates(new(CategoryId: category.Id, SourceRevision: categories.Revision));
        firstPage.Items.Should().HaveCount(50);
        firstPage.NextCursor.Should().NotBeNull();
        var nextPage = await Candidates(new(Cursor: firstPage.NextCursor!, CategoryId: category.Id,
            SourceRevision: categories.Revision));
        nextPage.Items.Should().HaveCount(4);
        firstPage.Items.Concat(nextPage.Items).Select(row => row.SelectionKey).Should().OnlyHaveUniqueItems();

        var selected = await ReadSelection(new(categories.Revision, [category.Id], []));
        selected.Items.Should().HaveCount(54);
        selected.Categories.Single(row => row.CategoryId == category.Id).SelectedItemCount.Should().Be(54);
        selected.Items.Where(row => row.ProductId == variantOnly.Id).Select(row => row.VariationId)
            .Should().BeEquivalentTo([firstVariation.Id, secondVariation.Id]);
        selected.Items.Should().NotContain(row => row.ProductId == variantOnly.Id && row.VariationId == null);
    }

    [Fact]
    public async Task NegativeOverrideSurvivesReloadAndSourceCheckExplainsRemovedReferences()
    {
        var originalCategory = NewCategory("Selection source category");
        var stableCategory = NewCategory("Retained override category");
        var moved = Product(originalCategory, "Moved product");
        var removed = Product(originalCategory, "Removed product");
        var retained = Product(stableCategory, "Retained negative override");
        var unsupported = Product(stableCategory, "Unsupported variation");
        var unsupportedVariation = new ProductVariation
        {
            Id = Guid.NewGuid(),
            Name = "Untranslated",
            CreatedBy = "test"
        };
        unsupported.Variations.Add(unsupportedVariation);
        await Save(originalCategory, [moved, removed, retained, unsupported], stableCategory);
        await AuthenticateForCatalogue();
        var initial = await ReadCategories();
        var negative = new ChannelCatalogueItemOverrideRequest(retained.Id, null, stableCategory.Id, false);
        var unsupportedNegative = new ChannelCatalogueItemOverrideRequest(
            unsupported.Id, unsupportedVariation.Id, stableCategory.Id, false);
        var request = new ChannelCatalogueSelectionSnapshotRequest(initial.Revision,
            [originalCategory.Id, stableCategory.Id], [negative, unsupportedNegative]);

        var first = await ReadSelection(request);
        first.Items.Should().Contain(row => row.ProductId == removed.Id);
        first.Items.Should().Contain(row => row.ProductId == moved.Id);
        first.Items.Should().NotContain(row => row.ProductId == retained.Id);
        first.Items.Should().NotContain(row => row.ProductId == unsupported.Id && row.VariationId == unsupportedVariation.Id);
        first.ItemOverrides.Should().HaveCount(2);
        var reloaded = await ReadSelection(request);
        reloaded.Items.Select(row => row.SelectionKey).Should().Equal(first.Items.Select(row => row.SelectionKey));
        reloaded.ItemOverrides.Should().BeEquivalentTo(first.ItemOverrides);

        var replacement = NewCategory("Replacement source category");
        await using (var context = DatabaseFixture.CreateContext())
        {
            var category = await context.Categories.SingleAsync(row => row.Id == originalCategory.Id);
            category.IsDeleted = true;
            context.Categories.Add(replacement);
            var assignment = await context.Set<ProductCategory>().SingleAsync(row => row.ProductId == moved.Id);
            assignment.CategoryId = replacement.Id;
            assignment.Category = replacement;
            var deletedProduct = await context.Products.SingleAsync(row => row.Id == removed.Id);
            deletedProduct.IsDeleted = true;
            await context.SaveChangesAsync();
        }

        using var stale = await PostIncludingNulls(SelectionPath, request);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(stale)).Should().Be("SourceRevisionChanged");

        var check = await ReadChanges(new(initial.Revision, [originalCategory.Id, stableCategory.Id],
            [new(moved.Id, null, originalCategory.Id), new(removed.Id, null, originalCategory.Id),
                new(retained.Id, null, stableCategory.Id), new(unsupported.Id, unsupportedVariation.Id, stableCategory.Id)],
            [negative, unsupportedNegative, new(removed.Id, null, originalCategory.Id, false)]));
        check.SourceChanged.Should().BeTrue();
        check.MaximumCategoryCount.Should().Be(1_000);
        check.MaximumItemOverrideCount.Should().Be(2_000);
        check.RemovedCategoryIds.Should().ContainSingle().Which.Should().Be(originalCategory.Id);
        check.RemovedItems.Should().HaveCount(2);
        check.RemovedItems.Single(row => row.ProductId == moved.Id).Reason.Should().Be("categoryRemoved");
        check.RemovedItems.Single(row => row.ProductId == moved.Id).CurrentCategoryId.Should().Be(replacement.Id);
        check.RemovedItems.Single(row => row.ProductId == removed.Id).Reason.Should().Be("itemRemoved");
        check.RemovedItemOverrides.Should().ContainSingle(row => row.ProductId == removed.Id && row.Reason == "itemRemoved");
        check.ItemStatuses.Should().ContainSingle(row => row.ProductId == retained.Id && row.Supported
            && row.CategoryId == stableCategory.Id && row.CurrentCategoryId == stableCategory.Id);
        check.ItemStatuses.Should().ContainSingle(row => row.ProductId == unsupported.Id
            && row.VariationId == unsupportedVariation.Id && !row.Supported
            && row.CurrentCategoryId == stableCategory.Id);
        check.ItemStatuses.Should().ContainSingle(row => row.ProductId == moved.Id
            && row.CategoryId == originalCategory.Id && row.CurrentCategoryId == replacement.Id);
        check.ItemStatuses.Should().NotContain(row => row.ProductId == removed.Id);
    }

    [Fact]
    public async Task SelectionLimitFailsClosedAndCatalogueRoutesRequireDedicatedScope()
    {
        var category = NewCategory("Selection cap fixture");
        var products = Enumerable.Range(0, 201).Select(index => Product(category, $"Capped item {index}")).ToList();
        await Save(category, products);
        await AuthenticateForCatalogue();
        var categories = await ReadCategories();
        categories.Categories.Single(row => row.CategoryId == category.Id).TotalItemCount.Should().Be(201);

        using var overflow = await PostIncludingNulls(SelectionPath,
            new ChannelCatalogueSelectionSnapshotRequest(categories.Revision, [category.Id], []));
        overflow.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(overflow)).Should().Be("SelectionLimitExceeded");

        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.ChannelOrdersWrite]));
        using var forbidden = await Client.PostAsync(CategoriesPath, content: null);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OmittedSelectedIsRejectedWhileExplicitFalsePreservesDeselection()
    {
        var category = NewCategory("Required selection fixture");
        var deselected = Product(category, "Explicitly deselected item");
        var retained = Product(category, "Retained item");
        await Save(category, [deselected, retained]);
        await AuthenticateForCatalogue();
        var source = await ReadCategories();
        var request = new
        {
            expectedSourceRevision = source.Revision,
            categoryIds = new[] { category.Id },
            itemOverrides = new[] { new { productId = deselected.Id, variationId = (Guid?)null, categoryId = category.Id } }
        };
        using var omitted = await Client.PostAsJsonAsync(SelectionPath, request);
        omitted.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var explicitFalse = await ReadSelection(new(source.Revision, [category.Id],
            [new(deselected.Id, null, category.Id, false)]));
        explicitFalse.Items.Should().ContainSingle(row => row.ProductId == retained.Id);
        explicitFalse.Items.Should().NotContain(row => row.ProductId == deselected.Id);
        explicitFalse.ItemOverrides.Should().ContainSingle(row => row.ProductId == deselected.Id && !row.Selected);
    }

    [Fact]
    public async Task CategoryAndOverrideReferenceCapsReturnStableClientErrors()
    {
        await AuthenticateForCatalogue();
        var revision = new string('a', 64);
        var tooManyCategories = new ChannelCatalogueSelectionSnapshotRequest(revision,
            Enumerable.Range(0, 1_001).Select(_ => Guid.NewGuid()).ToArray(), []);
        using var categoryOverflow = await PostIncludingNulls(SelectionPath, tooManyCategories);
        categoryOverflow.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(categoryOverflow)).Should().Be("CategoryLimitExceeded");

        var categoryId = Guid.NewGuid();
        var tooManyOverrides = new ChannelCatalogueSelectionSnapshotRequest(revision, [],
            Enumerable.Range(0, 2_001).Select(_ => new ChannelCatalogueItemOverrideRequest(
                Guid.NewGuid(), null, categoryId, false)).ToArray());
        using var overrideOverflow = await PostIncludingNulls(SelectionPath, tooManyOverrides);
        overrideOverflow.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(overrideOverflow)).Should().Be("SelectionOverrideLimitExceeded");
    }

    [Fact]
    public async Task NullSelectionCollectionsReturnBadRequestInsteadOfServerError()
    {
        await AuthenticateForCatalogue();
        using var selection = await PostIncludingNulls(SelectionPath,
            new ChannelCatalogueSelectionSnapshotRequest(new string('a', 64), null!, null!));
        selection.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var comparison = await PostIncludingNulls(ChangesPath,
            new ChannelCatalogueCategoryReferencesRequest(new string('a', 64), [], null!, []));
        comparison.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OversizedTenantCategoryInventoryReturnsAnActionableError()
    {
        var first = NewCategory("Category bound fixture 0");
        var remaining = Enumerable.Range(1, 1_000).Select(index => NewCategory($"Category bound fixture {index}")).ToArray();
        await Save(first, [], remaining);
        await AuthenticateForCatalogue();

        using var response = await Client.PostAsync(CategoriesPath, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be("CategoryLimitExceeded");
    }

    private async Task AuthenticateForCatalogue()
        => AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.ChannelCatalogueRead]));

    private async Task<ChannelCatalogueCategoriesSnapshot> ReadCategories()
    {
        using var response = await Client.PostAsync(CategoriesPath, content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ChannelCatalogueCategoriesSnapshot>(JsonOptions))!;
    }

    private async Task<ChannelCatalogueSelectionSnapshot> ReadSelection(ChannelCatalogueSelectionSnapshotRequest request)
    {
        using var response = await PostIncludingNulls(SelectionPath, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ChannelCatalogueSelectionSnapshot>(JsonOptions))!;
    }

    private async Task<ChannelCatalogueCategoriesSnapshot> ReadChanges(ChannelCatalogueCategoryReferencesRequest request)
    {
        using var response = await PostIncludingNulls(ChangesPath, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ChannelCatalogueCategoriesSnapshot>(JsonOptions))!;
    }

    private async Task<ChannelCatalogueCandidatesDto> Candidates(GetChannelCatalogueCandidatesQuery query)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CustomMediator>().SendQuery(query);
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("errorCode").GetString();
    }

    private Task<HttpResponseMessage> PostIncludingNulls<T>(string path, T body)
    {
        var options = new JsonSerializerOptions(JsonOptions) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };
        var content = new StringContent(JsonSerializer.Serialize(body, options), Encoding.UTF8, "application/json");
        return Client.PostAsync(path, content);
    }

    private async Task Save(Category category, IReadOnlyList<Product> products, params Category[] additionalCategories)
    {
        await using var context = DatabaseFixture.CreateContext();
        context.Categories.AddRange(new[] { category }.Concat(additionalCategories));
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
    }

    private static Category NewCategory(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DisplayOrder = 10,
        CreatedBy = "test",
        Translations = [new() { LanguageCode = "en", Name = name, CreatedBy = "test" }]
    };

    private static Product Product(Category category, string name)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            BasePrice = 5m,
            CreatedBy = "test",
            Allergens = [],
            Descriptions = [new() { Name = name, Description = "Fixture description", Lang = "en", CreatedBy = "test" }]
        };
        product.ProductCategories.Add(new() { Category = category, CategoryId = category.Id, IsPrimary = true, CreatedBy = "test" });
        return product;
    }

    private static ProductVariation Variation(Product product, string name)
    {
        var variation = new ProductVariation
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedBy = "test",
            Descriptions = [new() { LanguageCode = "en", Name = name, CreatedBy = "test" }]
        };
        product.Variations.Add(variation);
        return variation;
    }
}
