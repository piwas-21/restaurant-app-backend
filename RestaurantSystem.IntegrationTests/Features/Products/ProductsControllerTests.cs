using FluentAssertions;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Products.Dtos;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;

namespace RestaurantSystem.IntegrationTests.Features.Products;

public class ProductsControllerTests : IntegrationTestBase
{
    public ProductsControllerTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }


    [Fact]
    public async Task GetProducts_ReturnsAllProducts()
    {
        // Act
        var response = await Client.GetAsync("/api/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await GetFromJsonAsync<ApiResponse<PagedResult<ProductSummaryDto>>>("/api/products");

        result.Should().NotBeNull();

        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data?.Items.Should().HaveCountGreaterOrEqualTo(2); // From seed data
    }
}
