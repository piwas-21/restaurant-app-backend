using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Catalog.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Catalog;

[Collection("Database Lane 1")]
public sealed class CatalogQueryBoundaryTests : IntegrationTestBase
{
    public CatalogQueryBoundaryTests(DatabaseFixture databaseFixture) : base(databaseFixture) { }

    [Fact]
    public async Task Exactly_5000_catalog_rows_succeed_but_5001_is_rejected()
    {
        await AddProductsAsync(4998);

        var withinLimit = await Client.GetAsync("/api/Catalog?page=1&pageSize=1");
        withinLimit.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await ReadResponseAsync<ApiResponse<PagedResult<CatalogOfferFamilyDto>>>(withinLimit);
        page!.Data!.TotalCount.Should().Be(5000);
        page.Data.Items.Should().ContainSingle();

        await AddProductsAsync(1);

        var overLimit = await Client.GetAsync("/api/Catalog?page=1&pageSize=1");
        overLimit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var failure = await ReadResponseAsync<ApiResponse<PagedResult<CatalogOfferFamilyDto>>>(overLimit);
        failure!.Success.Should().BeFalse();
        failure.Errors.Should().Contain(error => error.Contains("exceeds the supported limit of 5000"));
    }

    private async Task AddProductsAsync(int count)
    {
        await using var context = DatabaseFixture.CreateContext();
        var products = Enumerable.Range(0, count)
            .Select(index => new Product
            {
                Id = Guid.NewGuid(),
                Name = $"Boundary product {Guid.NewGuid():N}",
                BasePrice = index + 1,
                Type = ProductType.MainItem,
                IsActive = true,
                IsAvailable = true,
                Ingredients = [],
                Allergens = [],
                CreatedBy = "catalog-boundary-test"
            })
            .ToList();
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
    }
}
