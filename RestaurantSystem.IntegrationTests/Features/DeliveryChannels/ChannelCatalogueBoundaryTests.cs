using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

[Collection("Database Lane 2")]
public sealed class ChannelCatalogueBoundaryTests(DatabaseFixture fixture) : ExternalOrderTestBase(fixture)
{
    private const string CatalogueEndpoint = "/api/delivery-channels/catalogue/snapshot";

    [Theory]
    [InlineData("anonymous")]
    [InlineData("human-admin")]
    [InlineData("menu:read")]
    [InlineData("channels:orders:write")]
    [InlineData("channels:catalogue:read")]
    public async Task OnlyDedicatedMachineReadScopeCanRetrieveAvailability(string identity)
    {
        var request = await PrepareAvailability();
        AuthenticateAsAnonymous();
        if (identity == "human-admin") AuthenticateAsAdmin();
        else if (identity != "anonymous") AuthenticateWithToken(await SeedTokenAsync([identity]));
        var response = await PostAsJsonAsync(CatalogueEndpoint, request);
        response.StatusCode.Should().Be(identity switch
        {
            "channels:catalogue:read" => HttpStatusCode.OK,
            "menu:read" or "channels:orders:write" => HttpStatusCode.Forbidden,
            _ => HttpStatusCode.Unauthorized,
        });
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.ExternalOrderReferences.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("store")]
    [InlineData("provider")]
    [InlineData("currency")]
    [InlineData("environment")]
    [InlineData("disabled")]
    [InlineData("ambiguous")]
    [InlineData("tenant-currency")]
    public async Task SnapshotRequiresExactEnabledStoreAndTenantCurrency(string fault)
    {
        var request = await PrepareAvailability();
        var settings = Factory.Services.GetRequiredService<IOptions<DeliveryChannelSettings>>().Value;
        switch (fault)
        {
            case "store": request = request with { StoreId = "foreign-store" }; break;
            case "provider": request = request with { Provider = "different-provider" }; break;
            case "currency": request = request with { Currency = "EUR" }; break;
            case "environment": request = request with { IsSandbox = false }; break;
            case "disabled": settings.Enabled = false; break;
            case "ambiguous": settings.Stores.Add(settings.Stores[0]); break;
            case "tenant-currency":
                settings.Stores[0].Currency = "EUR"; request = request with { Currency = "EUR" }; break;
        }
        var response = await PostAsJsonAsync(CatalogueEndpoint, request);
        response.StatusCode.Should().Be(fault == "tenant-currency" ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("overflow")]
    [InlineData("duplicate")]
    [InlineData("empty-product")]
    [InlineData("empty-variation")]
    [InlineData("null-items")]
    [InlineData("null-item")]
    [InlineData("missing-environment")]
    [InlineData("unknown-field")]
    public async Task MalformedSelectionsAreRefused(string fault)
    {
        var request = await PrepareAvailability();
        var json = JsonSerializer.SerializeToNode(request, JsonOptions)!.AsObject();
        switch (fault)
        {
            case "empty": json["items"] = new JsonArray(); break;
            case "overflow":
                json["items"] = JsonSerializer.SerializeToNode(Enumerable.Range(0, 201)
                .Select(_ => new ChannelAvailabilitySelection(Guid.NewGuid(), null)), JsonOptions); break;
            case "duplicate": json["items"]!.AsArray().Add(json["items"]![0]!.DeepClone()); break;
            case "empty-product": json["items"]![0]!["productId"] = Guid.Empty.ToString(); break;
            case "empty-variation": json["items"]![0]!["variationId"] = Guid.Empty.ToString(); break;
            case "null-items": json["items"] = null; break;
            case "null-item": json["items"] = new JsonArray((JsonNode?)null); break;
            case "missing-environment": json.Remove("isSandbox"); break;
            case "unknown-field": json["providerCredentials"] = "untrusted-fixture"; break;
        }
        using var body = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json");
        (await Client.PostAsync(CatalogueEndpoint, body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<ChannelAvailabilityRequest> PrepareAvailability()
    {
        var order = await PrepareAsync();
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.ChannelCatalogueRead]));
        return new()
        {
            Provider = order.Provider,
            StoreId = order.StoreId,
            Currency = order.Currency,
            IsSandbox = true,
            Items = [new(order.Items[0].ProductId, null)]
        };
    }
}
