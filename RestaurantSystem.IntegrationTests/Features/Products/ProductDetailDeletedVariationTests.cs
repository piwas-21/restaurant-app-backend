using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Products;

/// <summary>
/// The owner's report: "deleting a variation still not working", on every tenant, after the editor's
/// save was fixed (frontend #638/#640).
///
/// <para>
/// It was never the save. Measured against a real stack: the PUT that omits a variation answers 200
/// and the row IS soft-deleted (<c>product_variations.is_deleted = true</c>), the LIST endpoint
/// stops returning it — and <c>GET /api/Products/{id}</c> keeps serving it forever. So the editor
/// re-fetched the row it had just deleted and the guest sheet kept offering it, which is
/// indistinguishable, from a screen, from a save that did nothing.
/// </para>
/// <para>
/// The cause is one missing filter: <c>GetProductByIdQuery</c> calls <c>IgnoreQueryFilters()</c>,
/// which un-filters every INCLUDE, and the variations include carried no <c>!IsDeleted</c> of its
/// own — the same shape §9.14 found on categories and the images include already guards against.
/// </para>
/// </summary>
[Collection("Database Lane 2")]
public class ProductDetailDeletedVariationTests : IntegrationTestBase
{
    private Guid _productId;
    private Guid _categoryId;
    private Guid _keptVariationId;
    private Guid _deletedVariationId;

    public ProductDetailDeletedVariationTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    /// <summary>
    /// THE regression. Without the include filter the detail endpoint answers with BOTH variations
    /// after a save that deleted one; with it, only the surviving row comes back.
    /// </summary>
    [Fact]
    public async Task ADeletedVariation_IsGoneFromTheProductDetail()
    {
        AuthenticateAsAdmin();
        (await PutKeepingOnlyTheFirstVariationAsync()).StatusCode.Should().Be(HttpStatusCode.OK);

        var names = await ReadDetailVariationNamesAsync();

        names.Should().NotContain("Small");
    }

    /// <summary>
    /// The control that keeps the assertion above from passing vacuously: the endpoint must still
    /// return the variation that was NOT deleted. A filter that dropped every row would satisfy the
    /// regression and break every product with variations.
    /// </summary>
    [Fact]
    public async Task TheSurvivingVariation_IsStillServed()
    {
        AuthenticateAsAdmin();
        (await PutKeepingOnlyTheFirstVariationAsync()).StatusCode.Should().Be(HttpStatusCode.OK);

        var names = await ReadDetailVariationNamesAsync();

        names.Should().ContainSingle().Which.Should().Be("Large");
    }

    /// <summary>
    /// The control that names WHICH half was broken. It fails on the code this fix replaces too —
    /// deliberately: it proves the write worked, so a reader cannot re-diagnose this as a save that
    /// was never sent. That was the first hypothesis, and it was wrong.
    /// </summary>
    [Fact]
    public async Task TheDeleteItself_ReachesTheDatabase()
    {
        AuthenticateAsAdmin();
        (await PutKeepingOnlyTheFirstVariationAsync()).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var deleted = await context.ProductVariations
            .IgnoreQueryFilters()
            .SingleAsync(v => v.Id == _deletedVariationId);

        deleted.IsDeleted.Should().BeTrue();
    }

    // ---- helpers -------------------------------------------------------------------------------

    private Task<HttpResponseMessage> PutKeepingOnlyTheFirstVariationAsync() =>
        Client.PutAsync(
            $"/api/Products/{_productId}",
            new StringContent(PayloadKeepingOnly(_keptVariationId), Encoding.UTF8, "application/json"));

    private async Task<List<string>> ReadDetailVariationNamesAsync()
    {
        var response = await Client.GetAsync($"/api/Products/{_productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<ProductDetailEnvelope>();
        return payload!.Data.Variations.Select(v => v.Name).ToList();
    }

    private string PayloadKeepingOnly(Guid variationId) => $$"""
    {
      "id": "{{_productId}}",
      "name": "Deleted Variation Pizza",
      "basePrice": 15,
      "isActive": true,
      "isAvailable": true,
      "isSpecial": false,
      "preparationTimeMinutes": 10,
      "type": "mainItem",
      "kitchenType": "none",
      "displayOrder": 0,
      "categoryIds": ["{{_categoryId}}"],
      "primaryCategoryId": "{{_categoryId}}",
      "variations": [
        {
          "id": "{{variationId}}",
          "name": "Large",
          "priceModifier": 2.0,
          "isActive": true,
          "displayOrder": 0
        }
      ]
    }
    """;

    private sealed record ProductDetailEnvelope(ProductDetailPayload Data);

    private sealed record ProductDetailPayload(List<VariationPayload> Variations);

    private sealed record VariationPayload(string Name);

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _categoryId = (await context.Categories.OrderBy(c => c.Name).FirstAsync()).Id;

        var product = new Product
        {
            Name = "Deleted Variation Pizza",
            BasePrice = 15m,
            IsActive = true,
            IsAvailable = true,
            Type = ProductType.MainItem,
            CreatedBy = "test",
        };
        product.ProductCategories.Add(new ProductCategory
        {
            CategoryId = _categoryId,
            IsPrimary = true,
            CreatedBy = "test",
        });

        var kept = new ProductVariation
        {
            Name = "Large",
            PriceModifier = 2m,
            IsActive = true,
            DisplayOrder = 0,
            CreatedBy = "test",
        };
        var removed = new ProductVariation
        {
            Name = "Small",
            PriceModifier = -2m,
            IsActive = true,
            DisplayOrder = 1,
            CreatedBy = "test",
        };
        product.Variations.Add(kept);
        product.Variations.Add(removed);

        context.Add(product);
        await context.SaveChangesAsync();

        _productId = product.Id;
        _keptVariationId = kept.Id;
        _deletedVariationId = removed.Id;
    }
}
