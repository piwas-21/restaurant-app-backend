using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

public abstract class CustomerBasketMutationFixture(DatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    protected string Session { get; } = Guid.NewGuid().ToString("D");
    protected Guid ProductId { get; private set; }
    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.PostConfigure<CustomerMobileSettings>(settings =>
        {
            settings.Enabled = true;
            settings.TenantId = Guid.NewGuid();
            settings.Lifecycle = "active";
            settings.EnabledCapabilities = ["ordering"];
        });

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = new Product
        {
            Name = "Native mutation fixture",
            BasePrice = 7m,
            IsActive = true,
            IsAvailable = true,
            Ingredients = [],
            Allergens = [],
            CreatedBy = nameof(CustomerBasketMutationFixture)
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        ProductId = product.Id;
        AuthenticateAsAnonymous();
    }

    protected async Task<BasketDto> AddAsync(Guid? operationId = null) =>
        await BasketAsync(await SendAsync("/api/basket/items", new AddToBasketDto
        {
            ProductId = ProductId,
            Quantity = 2,
            OperationId = operationId
        }));

    protected static CustomerBasketItemMutationDto Update(BasketDto basket) => new()
    {
        OperationId = Guid.NewGuid(),
        ExpectedBasketId = basket.Id,
        ExpectedPurchaseFingerprint = basket.PurchaseFingerprint,
        Action = CustomerBasketItemAction.Update,
        Quantity = 3,
        SpecialInstructions = "No salt"
    };

    protected Task<HttpResponseMessage> MutateAsync(BasketDto basket, CustomerBasketItemMutationDto request,
        Guid? itemId = null, string? session = null) =>
        SendAsync($"/api/customer-mobile/basket/items/{itemId ?? basket.Items[0].Id}/operations", request, session);

    protected async Task<HttpResponseMessage> SendAsync<T>(string path, T body, string? session = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        request.Headers.Add("X-Session-Id", session ?? Session);
        return await Client.SendAsync(request);
    }

    protected static async Task<BasketDto> BasketAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<BasketDto>>(JsonOptions);
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        return envelope.Data!;
    }

    protected async Task<List<CustomerBasketOperation>> ReceiptsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Set<CustomerBasketOperation>().AsNoTracking().ToListAsync();
    }
}
