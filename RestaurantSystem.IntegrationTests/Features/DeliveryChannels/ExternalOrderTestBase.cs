using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Features.DeliveryChannels.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Features.ApiTokens;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.DeliveryChannels;

public abstract class ExternalOrderTestBase(DatabaseFixture fixture) : ApiTokenScopeTestBase(fixture), IAsyncLifetime
{
    protected const string Endpoint = "/api/delivery-channels/orders";
    private string? _originalCurrency;
    private bool _currencySeeded;

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        var tenant = await context.RestaurantInfo.SingleAsync();
        _originalCurrency = tenant.Currency;
        tenant.Currency = "CHF";
        await context.SaveChangesAsync();
        _currencySeeded = true;
    }

    public new async Task DisposeAsync()
    {
        try
        {
            if (_currencySeeded)
            {
                await using var context = DatabaseFixture.CreateContext();
                var tenant = await context.RestaurantInfo.SingleAsync();
                tenant.Currency = _originalCurrency;
                await context.SaveChangesAsync();
            }
        }
        finally { await base.DisposeAsync(); }
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.PostConfigure<DeliveryChannelSettings>(settings =>
        {
            settings.Enabled = true;
            settings.SandboxOnly = true;
            settings.Stores = [new() { Provider = "uber-eats", StoreId = "approved-test-store", Currency = "CHF", IsSandbox = true }];
        });
    }

    protected async Task<ExternalOrderRequest> PrepareAsync()
    {
        AuthenticateWithToken(await SeedTokenAsync([ApiTokenScopes.ChannelOrdersWrite]));
        await using var context = DatabaseFixture.CreateContext();
        var product = await context.Products.FirstAsync();
        return new()
        {
            Provider = "uber-eats",
            StoreId = "approved-test-store",
            ExternalOrderId = "provider-order-1",
            DisplayId = "9116D",
            Currency = "CHF",
            CanonicalOrderHash = new string('a', 64),
            MerchantTotal = 5,
            PlacedAt = new DateTimeOffset(2026, 10, 1, 19, 25, 7, TimeSpan.FromHours(2)),
            FulfillmentType = "DELIVERY_BY_UBER",
            CustomerName = "Sandbox guest",
            Instructions = "No food or courier.\nVerify the cart instructions.",
            Items = [new() { ProductId = product.Id, Name = "Marketplace meal", Quantity = 1, UnitPrice = 5, Total = 5,
                Instructions = "No peanuts — allergy instruction fixture." }],
        };
    }

    protected async Task<ExternalOrderImportDto> ImportAsync(ExternalOrderRequest request)
    {
        var response = await PostAsJsonAsync(Endpoint, request);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ExternalOrderImportDto>(JsonOptions))!;
    }
}
