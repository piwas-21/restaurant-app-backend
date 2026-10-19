using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Basket.Dtos;
using RestaurantSystem.Api.Features.Basket.Dtos.Requests;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Basket;

[Collection("Database Lane 2")]
public sealed class CustomerBasketOperationTests(DatabaseFixture databaseFixture) : IntegrationTestBase(databaseFixture)
{
    private Guid _productId;
    private readonly string _session = Guid.NewGuid().ToString("D");

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
            Name = "Operation fixture",
            BasePrice = 7m,
            IsActive = true,
            IsAvailable = true,
            Ingredients = [],
            Allergens = [],
            CreatedBy = nameof(CustomerBasketOperationTests)
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        _productId = product.Id;
        AuthenticateAsAnonymous();
    }

    [Fact]
    public async Task Sequential_replay_from_new_http_requests_adds_one_line_once()
    {
        var request = Request(Guid.NewGuid());
        var first = await SendAsync(request);
        var replay = await SendAsync(request);
        var firstBasket = await BasketAsync(first);
        var replayBasket = await BasketAsync(replay);
        replayBasket.Id.Should().Be(firstBasket.Id);
        replayBasket.Items.Single().Quantity.Should().Be(2);
        replayBasket.SubTotal.Should().Be(14m);
        (await OperationsAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task Concurrent_identical_submissions_have_one_durable_effect()
    {
        var request = Request(Guid.NewGuid());
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SendAsync(request)));
        foreach (var response in responses)
            (await BasketAsync(response)).Items.Single().Quantity.Should().Be(2);
        (await OperationsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Different_operations_on_one_owner_serialize_basket_creation_and_quantity_changes()
    {
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SendAsync(Request(Guid.NewGuid()))));
        foreach (var response in responses) (await BasketAsync(response)).Id.Should().NotBeEmpty();
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var basket = await context.Baskets.Include(row => row.Items).SingleAsync(row => row.SessionId == _session);
        basket.Items.Single().Quantity.Should().Be(8);
        basket.SubTotal.Should().Be(56m);
        (await OperationsAsync()).Should().HaveCount(4);
    }

    [Fact]
    public async Task Changed_request_conflicts_and_another_session_cannot_recover_the_result()
    {
        var request = Request(Guid.NewGuid());
        await BasketAsync(await SendAsync(request));
        (await SendAsync(request with { Quantity = 3 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SendAsync(request, Guid.NewGuid().ToString("D"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BasketAsync(await SendAsync(request))).Items.Single().Quantity.Should().Be(2);
    }

    [Fact]
    public async Task Failed_validation_or_product_lookup_leaves_no_receipt_or_partial_basket()
    {
        var operationId = Guid.NewGuid();
        (await SendAsync(Request(operationId) with { Quantity = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SendAsync(Request(operationId) with { ProductId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await OperationsAsync()).Should().BeEmpty();
        using (var scope = Factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Baskets.CountAsync()).Should().Be(0);
        (await BasketAsync(await SendAsync(Request(operationId)))).Items.Single().Quantity.Should().Be(2);
    }

    [Theory]
    [InlineData("not-a-session")]
    [InlineData("AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Recoverable_add_requires_nonempty_uuid_session(string session)
    {
        (await SendAsync(Request(Guid.NewGuid()), session)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await OperationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Rejected_uppercase_session_cannot_create_a_parallel_basket_or_receipt()
    {
        const string session = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
        var request = Request(Guid.NewGuid());
        (await SendAsync(request, session.ToUpperInvariant())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BasketAsync(await SendAsync(request, session))).Items.Single().Quantity.Should().Be(2);
        (await BasketAsync(await SendAsync(request, session))).Items.Single().Quantity.Should().Be(2);
        (await SendAsync(Request(Guid.NewGuid()), session.ToUpperInvariant())).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var verify = Factory.Services.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().Baskets.CountAsync()).Should().Be(1);
        (await OperationsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Empty_operation_is_rejected_and_legacy_adds_remain_independent()
    {
        (await SendAsync(Request(Guid.Empty))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await BasketAsync(await SendAsync(Request(null)));
        (await BasketAsync(await SendAsync(Request(null)))).Items.Single().Quantity.Should().Be(4);
        (await OperationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Erased_basket_does_not_erase_the_receipt_or_resurrect_an_add()
    {
        var request = Request(Guid.NewGuid());
        var basket = await BasketAsync(await SendAsync(request));
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await context.BasketItems.Where(row => row.BasketId == basket.Id).ExecuteDeleteAsync();
            await context.Baskets.Where(row => row.Id == basket.Id).ExecuteDeleteAsync();
        }
        (await SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await OperationsAsync()).Should().ContainSingle();
        using var verify = Factory.Services.CreateScope();
        (await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().Baskets.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Only_hashes_and_result_reference_are_persisted()
    {
        var operationId = Guid.NewGuid();
        var basket = await BasketAsync(await SendAsync(Request(operationId) with { SpecialInstructions = "No salt" }));
        var receipt = (await OperationsAsync()).Single();
        receipt.OperationId.Should().Be(operationId);
        receipt.BasketId.Should().Be(basket.Id);
        receipt.OwnerHash.Should().MatchRegex("^[0-9a-f]{64}$");
        receipt.PayloadHash.Should().MatchRegex("^[0-9a-f]{64}$");
        receipt.OwnerHash.Should().NotContain(_session);
    }

    [Fact]
    public async Task Customer_and_guest_receipts_are_separate_even_with_the_same_session()
    {
        var guestRequest = Request(Guid.NewGuid());
        await BasketAsync(await SendAsync(guestRequest));
        AuthenticateAsUser();
        (await SendAsync(guestRequest)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var customerRequest = Request(Guid.NewGuid());
        (await BasketAsync(await SendAsync(customerRequest))).Items.Single().Quantity.Should().Be(2);
        (await BasketAsync(await SendAsync(customerRequest))).Items.Single().Quantity.Should().Be(2);
        (await SendAsync(customerRequest, Guid.NewGuid().ToString("D"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        AuthenticateAsAnonymous();
        (await SendAsync(customerRequest)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BasketAsync(await SendAsync(guestRequest))).Items.Single().Quantity.Should().Be(2);
    }

    [Fact]
    public async Task Accepted_receipts_recover_after_opt_out_but_new_operations_are_unavailable()
    {
        var request = Request(Guid.NewGuid());
        await BasketAsync(await SendAsync(request));
        using (var scope = Factory.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<IOptions<CustomerMobileSettings>>().Value.Enabled = false;
        (await SendAsync(Request(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BasketAsync(await SendAsync(request))).Items.Single().Quantity.Should().Be(2);
        (await OperationsAsync()).Should().ContainSingle();
    }

    [Theory]
    [InlineData("Bearer expired-customer-token")]
    [InlineData("Basic rejected-customer-credentials")]
    public async Task Failed_authentication_cannot_downgrade_a_customer_operation_to_a_guest(string authorization)
    {
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization).Should().BeTrue();
        (await SendAsync(Request(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await OperationsAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.KitchenStaff)]
    public async Task Staff_cannot_use_the_customer_operation_contract(UserRole role)
    {
        AuthenticateAsRole(role);
        (await SendAsync(Request(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await OperationsAsync()).Should().BeEmpty();
    }

    private AddToBasketDto Request(Guid? operationId) => new() { OperationId = operationId, ProductId = _productId, Quantity = 2 };

    private async Task<HttpResponseMessage> SendAsync(AddToBasketDto request, string? session = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/basket/items", UriKind.Relative))
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        message.Headers.Add("X-Session-Id", session ?? _session);
        return await Client.SendAsync(message);
    }

    private static async Task<BasketDto> BasketAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<BasketDto>>(JsonOptions);
        envelope!.Success.Should().BeTrue();
        envelope.Data.Should().NotBeNull();
        return envelope.Data!;
    }

    private async Task<List<CustomerBasketOperation>> OperationsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Set<CustomerBasketOperation>().AsNoTracking().ToListAsync();
    }
}
