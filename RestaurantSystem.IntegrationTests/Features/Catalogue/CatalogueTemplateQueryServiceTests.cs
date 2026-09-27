using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.Catalogue.Dtos;
using RestaurantSystem.Api.Features.Catalogue.Services;

namespace RestaurantSystem.IntegrationTests.Features.Catalogue;

public sealed class CatalogueTemplateQueryServiceTests
{
    [Fact]
    public async Task Invalid_page_filter_returns_bad_request_without_calling_central_catalogue()
    {
        var central = new Mock<ICentralCatalogueClient>(MockBehavior.Strict);
        var preferences = new Mock<ICatalogueCuisinePreferencesService>(MockBehavior.Strict);
        var currentUser = new Mock<ICurrentUserService>(MockBehavior.Strict);
        var service = new CatalogueTemplateQueryService(central.Object, preferences.Object, currentUser.Object);

        var response = await service.GetPageAsync("unknown", null, null, null, null, null, CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        response.Body.GetProperty("error").GetString().Should().Be("invalid_query");
        central.VerifyNoOtherCalls();
        preferences.VerifyNoOtherCalls();
        currentUser.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Anonymous_page_proxy_preserves_public_response_and_uses_default_limit()
    {
        var expectedBody = Json("""{"items":[{"templateId":"central-first"}],"nextCursor":"opaque"}""");
        var expectedResponse = new CatalogueProxyResponse(StatusCodes.Status200OK, expectedBody);
        var central = new Mock<ICentralCatalogueClient>(MockBehavior.Strict);
        central.Setup(client => client.GetPageAsync(
                new CataloguePageQuery("item", "turkish", "kofte", "tr", 24, "opaque"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);
        var preferences = new Mock<ICatalogueCuisinePreferencesService>(MockBehavior.Strict);
        var currentUser = new Mock<ICurrentUserService>(MockBehavior.Strict);
        currentUser.SetupGet(user => user.IsAdmin).Returns(false);
        var service = new CatalogueTemplateQueryService(central.Object, preferences.Object, currentUser.Object);

        var response = await service.GetPageAsync(
            "item", "turkish", "kofte", "tr", null, "opaque", CancellationToken.None);

        response.Should().BeSameAs(expectedResponse);
        central.VerifyAll();
        preferences.VerifyNoOtherCalls();
        currentUser.VerifyGet(user => user.IsAdmin, Times.Once);
    }

    [Fact]
    public async Task Admin_page_ranks_saved_cuisines_without_changing_tie_order_or_cursor()
    {
        var central = new Mock<ICentralCatalogueClient>(MockBehavior.Strict);
        central.Setup(client => client.GetPageAsync(
                It.IsAny<CataloguePageQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogueProxyResponse(StatusCodes.Status200OK, Json("""
                {
                  "items": [
                    {"templateId":"unmatched","cuisines":["greek"]},
                    {"templateId":"preferred-first","cuisines":["turkish"]},
                    {"templateId":"preferred-second","cuisines":["turkish"]}
                  ],
                  "nextCursor":"opaque-next"
                }
                """)));
        var preferences = new Mock<ICatalogueCuisinePreferencesService>(MockBehavior.Strict);
        preferences.Setup(service => service.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogueCuisinePreferencesDto(["turkish"]));
        var currentUser = new Mock<ICurrentUserService>(MockBehavior.Strict);
        currentUser.SetupGet(user => user.IsAdmin).Returns(true);
        var service = new CatalogueTemplateQueryService(central.Object, preferences.Object, currentUser.Object);

        var response = await service.GetPageAsync(null, null, null, null, 24, null, CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.Body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("templateId").GetString())
            .Should().Equal("preferred-first", "preferred-second", "unmatched");
        response.Body.GetProperty("nextCursor").GetString().Should().Be("opaque-next");
        central.VerifyAll();
        preferences.VerifyAll();
        currentUser.VerifyGet(user => user.IsAdmin, Times.Once);
    }

    [Fact]
    public async Task Invalid_revision_route_input_returns_not_found_without_upstream_call()
    {
        var central = new Mock<ICentralCatalogueClient>(MockBehavior.Strict);
        var preferences = new Mock<ICatalogueCuisinePreferencesService>(MockBehavior.Strict);
        var currentUser = new Mock<ICurrentUserService>(MockBehavior.Strict);
        var service = new CatalogueTemplateQueryService(central.Object, preferences.Object, currentUser.Object);

        var response = await service.GetRevisionAsync("not a slug", 1, CancellationToken.None);

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        response.Body.GetProperty("error").GetString().Should().Be("not_found");
        central.VerifyNoOtherCalls();
        preferences.VerifyNoOtherCalls();
        currentUser.VerifyNoOtherCalls();
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
