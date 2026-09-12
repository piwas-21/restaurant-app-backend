using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class StaffCounterOrderTests : IntegrationTestBase
{
    private Guid _productId;
    private Guid _customerId;

    public StaffCounterOrderTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Anonymous_cannot_reach_the_staff_counter_contract()
    {
        AuthenticateAsAnonymous();
        var response = await Client.PostAsJsonAsync("/api/staff/orders",
            CreateBody(Guid.NewGuid(), releaseToKitchen: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Quote_matches_legacy_server_pricing_without_persisting_an_order()
    {
        AuthenticateAsAnonymous();
        var legacy = await Client.PostAsJsonAsync("/api/Orders", new
        {
            type = "Takeaway",
            items = new[] { new { productId = _productId, quantity = 1, unitPrice = 0.01m } }
        });
        var legacyBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(legacy))!;

        AuthenticateAsAdmin();
        var quote = await PostAsJsonAsync("/api/staff/orders/quote", CreateBody(Guid.NewGuid(), false));
        var quoteBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(quote))!;

        quoteBody.Success.Should().BeTrue();
        quoteBody.Data!.Total.Should().Be(legacyBody.Data!.Total);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1, "the quote itself is read-only");
    }

    [Fact]
    public async Task Positive_points_are_rejected_by_quote_and_create_before_any_mutation()
    {
        AuthenticateAsAdmin();
        var quote = await PostAsJsonAsync(
            "/api/staff/orders/quote", CreateBody(Guid.NewGuid(), false, _customerId, pointsToRedeem: 1));
        var create = await PostAsJsonAsync(
            "/api/staff/orders", CreateBody(Guid.NewGuid(), false, _customerId, pointsToRedeem: 1));

        quote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_uses_catalogue_price_and_holds_unpaid_order()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/staff/orders", CreateBody(Guid.NewGuid(), false));
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;

        body.Success.Should().BeTrue();
        body.Data!.Total.Should().Be(12.99m);
        body.Data.PaymentStatus.Should().Be(nameof(PaymentStatus.Pending));
        body.Data.IsKitchenReleased.Should().BeFalse();
        body.Data.Version.Should().Be(1);
        body.Data.UserId.Should().BeNull();

        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(1);
        (await context.Orders.Select(order => order.IsKitchenReleased).SingleAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task DineIn_create_assigns_only_the_matching_open_service_session()
    {
        var sessionId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.TableServiceSessions.Add(new TableServiceSession
            {
                Id = sessionId,
                TableNumber = 9,
                Status = TableServiceSessionStatus.Open,
                Version = 1,
                OpenedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });
            await context.SaveChangesAsync();
        }

        AuthenticateAsAdmin();
        var accepted = await PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = "DineIn",
            tableNumber = 9,
            serviceSessionId = sessionId,
            paymentState = "PayLater",
            items = new[] { new { productId = _productId, quantity = 1 } }
        });
        var acceptedBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(accepted))!;

        acceptedBody.Success.Should().BeTrue();
        acceptedBody.Data!.ServiceSessionId.Should().Be(sessionId);

        var rejected = await PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = "DineIn",
            tableNumber = 10,
            serviceSessionId = sessionId,
            paymentState = "PayLater",
            items = new[] { new { productId = _productId, quantity = 1 } }
        });
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await using var verification = DatabaseFixture.CreateContext();
        (await verification.Orders.CountAsync()).Should().Be(1);
        (await verification.Orders.SingleAsync()).ServiceSessionId.Should().Be(sessionId);
        (await verification.StaffOrderOperations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Retry_returns_original_order_and_payload_mismatch_banks_nothing_else()
    {
        AuthenticateAsAdmin();
        var operationId = Guid.NewGuid();
        var first = await PostAsJsonAsync("/api/staff/orders", CreateBody(operationId, false));
        var firstBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(first))!;
        var retry = await PostAsJsonAsync("/api/staff/orders", CreateBody(operationId, false));
        var retryBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(retry))!;

        retryBody.Success.Should().BeTrue();
        retryBody.Data!.Id.Should().Be(firstBody.Data!.Id);

        var mismatch = await PostAsJsonAsync("/api/staff/orders", CreateBody(operationId, true));
        var mismatchBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(mismatch))!;
        mismatchBody.Success.Should().BeFalse();
        mismatchBody.ErrorCode.Should().Be(ErrorCodes.StaffOrderOperationPayloadMismatch);

        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Channel_and_table_shape_are_validated_before_create()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = "Takeaway",
            tableNumber = 7,
            items = new[] { new { productId = _productId, quantity = 1 } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Customer_association_is_authorized_and_server_authored()
    {
        AuthenticateAsRole(UserRole.Server);
        var refused = await PostAsJsonAsync("/api/staff/orders", CreateBody(
            Guid.NewGuid(), false, _customerId));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        AuthenticateAsAdmin();
        var accepted = await PostAsJsonAsync("/api/staff/orders", CreateBody(
            Guid.NewGuid(), false, _customerId));
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(accepted))!;
        body.Data!.UserId.Should().Be(_customerId);
        body.Data.CustomerName.Should().Be("Test User");
    }

    [Fact]
    public async Task Release_requires_current_version_and_is_idempotent()
    {
        AuthenticateAsAdmin();
        var create = await PostAsJsonAsync("/api/staff/orders", CreateBody(Guid.NewGuid(), false));
        var created = (await ReadResponseAsync<ApiResponse<OrderDto>>(create))!.Data!;

        var stale = await PostAsJsonAsync($"/api/staff/orders/{created.Id}/release", new
        { clientOperationId = Guid.NewGuid(), expectedVersion = created.Version + 1 });
        var staleBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(stale))!;
        staleBody.ErrorCode.Should().Be(ErrorCodes.StaffOrderVersionConflict);

        var operationId = Guid.NewGuid();
        var release = await PostAsJsonAsync($"/api/staff/orders/{created.Id}/release", new
        { clientOperationId = operationId, expectedVersion = created.Version });
        var released = (await ReadResponseAsync<ApiResponse<OrderDto>>(release))!;
        released.Data!.IsKitchenReleased.Should().BeTrue();
        released.Data.Version.Should().Be(2);
        released.Data.Status.Should().Be(nameof(OrderStatus.Confirmed));

        var retry = await PostAsJsonAsync($"/api/staff/orders/{created.Id}/release", new
        { clientOperationId = operationId, expectedVersion = created.Version });
        var retryBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(retry))!;
        retryBody.Success.Should().BeTrue();
        retryBody.Data!.Id.Should().Be(created.Id);

        var repeatedIntent = await PostAsJsonAsync($"/api/staff/orders/{created.Id}/release", new
        { clientOperationId = Guid.NewGuid(), expectedVersion = created.Version });
        var repeatedBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(repeatedIntent))!;
        repeatedBody.Success.Should().BeTrue();
        repeatedBody.Data!.Version.Should().Be(2);
    }

    private object CreateBody(
        Guid operationId, bool releaseToKitchen, Guid? customerId = null, int? pointsToRedeem = null) => new
        {
            clientOperationId = operationId,
            releaseToKitchen,
            type = "Takeaway",
            customerUserId = customerId,
            pointsToRedeem,
            paymentState = "PayLater",
            items = new[] { new { productId = _productId, quantity = 1, unitPrice = 0.01m } }
        };

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _productId = await context.Products
            .Where(product => product.Name == "Test Pizza")
            .Select(product => product.Id)
            .SingleAsync();
        _customerId = Guid.Parse(RestaurantSystem.IntegrationTests.Common.TestAuthHandler.UserId);
    }
}
