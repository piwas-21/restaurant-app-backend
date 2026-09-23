using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common.Constants;
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
    public async Task Positive_points_quote_is_read_only_and_create_redeems_atomically()
    {
        await SeedPointsAsync(100);
        AuthenticateAsAdmin();
        var quote = await PostAsJsonAsync(
            "/api/staff/orders/quote", CreateBody(Guid.NewGuid(), false, _customerId, pointsToRedeem: 100));
        var quoteBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(quote))!;

        quote.StatusCode.Should().Be(HttpStatusCode.OK);
        quoteBody.Success.Should().BeTrue();
        quoteBody.Data!.FidelityPointsRedeemed.Should().Be(100);
        quoteBody.Data.FidelityPointsDiscount.Should().Be(1m);
        quoteBody.Data.Total.Should().Be(11.99m);

        await using (var afterQuote = DatabaseFixture.CreateContext())
        {
            (await afterQuote.FidelityPointBalances.SingleAsync()).CurrentPoints.Should().Be(100);
            (await afterQuote.FidelityPointsTransactions.CountAsync()).Should().Be(0);
            (await afterQuote.Orders.CountAsync()).Should().Be(0);
        }

        var create = await PostAsJsonAsync(
            "/api/staff/orders", CreateBody(Guid.NewGuid(), false, _customerId, pointsToRedeem: 100));
        var createBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(create))!;

        create.StatusCode.Should().Be(HttpStatusCode.OK);
        createBody.Success.Should().BeTrue();
        createBody.Data!.FidelityPointsRedeemed.Should().Be(100);
        createBody.Data.FidelityPointsDiscount.Should().Be(1m);
        createBody.Data.Total.Should().Be(11.99m);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(1);
        (await context.FidelityPointsTransactions.CountAsync()).Should().Be(1);
        (await context.FidelityPointBalances.SingleAsync()).CurrentPoints.Should().Be(0);
    }

    [Fact]
    public async Task Insufficient_points_roll_back_the_order_and_operation()
    {
        await SeedPointsAsync(50);
        AuthenticateAsRole(UserRole.Server);

        var response = await PostAsJsonAsync(
            "/api/staff/orders", CreateBody(Guid.NewGuid(), false, _customerId, pointsToRedeem: 100));
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Success.Should().BeFalse();
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(0);
        (await context.FidelityPointsTransactions.CountAsync()).Should().Be(0);
        (await context.FidelityPointBalances.SingleAsync()).CurrentPoints.Should().Be(50);
    }

    [Fact]
    public async Task Redemption_rejects_a_discount_larger_than_the_priced_order_in_quote_and_create()
    {
        await SeedPointsAsync(2_000);
        AuthenticateAsRole(UserRole.Server);
        var request = CreateBody(Guid.NewGuid(), false, _customerId, pointsToRedeem: 2_000);

        var quote = await PostAsJsonAsync("/api/staff/orders/quote", request);
        var create = await PostAsJsonAsync("/api/staff/orders", request);
        var quoteBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(quote))!;
        var createBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(create))!;

        quote.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        quoteBody.Errors.Should().ContainSingle().Which.Should()
            .Contain("cannot exceed the order's discountable amount");
        createBody.Errors.Should().BeEquivalentTo(quoteBody.Errors);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(0);
        (await context.FidelityPointsTransactions.CountAsync()).Should().Be(0);
        (await context.FidelityPointBalances.SingleAsync()).CurrentPoints.Should().Be(2_000);
    }

    [Fact]
    public async Task Concurrent_redemptions_cannot_spend_the_same_balance_twice()
    {
        await SeedPointsAsync(100);
        AuthenticateAsRole(UserRole.Cashier);
        var first = PostAsJsonAsync(
            "/api/staff/orders", CreateBody(Guid.NewGuid(), false, _customerId, pointsToRedeem: 100));
        var second = PostAsJsonAsync(
            "/api/staff/orders", CreateBody(Guid.NewGuid(), false, _customerId, pointsToRedeem: 100));

        var responses = await Task.WhenAll(first, second);
        responses.Select(response => response.StatusCode)
            .Should().ContainSingle(status => status == HttpStatusCode.OK);
        responses.Select(response => response.StatusCode)
            .Should().ContainSingle(status => status == HttpStatusCode.BadRequest);

        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(1);
        (await context.FidelityPointsTransactions.CountAsync()).Should().Be(1);
        (await context.FidelityPointBalances.SingleAsync()).CurrentPoints.Should().Be(0);
    }

    [Fact]
    public async Task Redemption_retry_replays_without_a_second_ledger_debit()
    {
        await SeedPointsAsync(100);
        AuthenticateAsRole(UserRole.Cashier);
        var operationId = Guid.NewGuid();
        var request = CreateBody(operationId, false, _customerId, pointsToRedeem: 100);

        var first = await PostAsJsonAsync("/api/staff/orders", request);
        var retry = await PostAsJsonAsync("/api/staff/orders", request);
        var firstBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(first))!;
        var retryBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(retry))!;

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        retryBody.Data!.Id.Should().Be(firstBody.Data!.Id);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(1);
        (await context.FidelityPointsTransactions.CountAsync()).Should().Be(1);
        (await context.FidelityPointBalances.SingleAsync()).CurrentPoints.Should().Be(0);
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
    public async Task Notes_at_the_column_limit_persist_and_longer_notes_are_rejected_before_persistence()
    {
        AuthenticateAsAdmin();
        var accepted = await PostAsJsonAsync(
            "/api/staff/orders", CreateBody(
                Guid.NewGuid(), false, notes: new string('x', OrderFieldLimits.NotesMaxLength)));
        var acceptedBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(accepted))!;

        acceptedBody.Success.Should().BeTrue();
        acceptedBody.Data!.Notes.Should().HaveLength(OrderFieldLimits.NotesMaxLength);

        var rejected = await PostAsJsonAsync(
            "/api/staff/orders", CreateBody(
                Guid.NewGuid(), false, notes: new string('x', OrderFieldLimits.NotesMaxLength + 1)));
        var rejectedBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(rejected))!;

        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        rejectedBody.Success.Should().BeFalse();
        rejectedBody.Errors.Should().Contain(
            $"Notes cannot exceed {OrderFieldLimits.NotesMaxLength} characters.");

        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.Orders.Select(order => order.Notes).SingleAsync())
            .Should().HaveLength(OrderFieldLimits.NotesMaxLength);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Dine_in_create_without_an_open_session_is_rejected()
    {
        var tableId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Tables.Add(new Table
            {
                Id = tableId,
                TableNumber = "T-QA",
                MaxGuests = 4,
                IsActive = true,
                CreatedBy = "test"
            });
            await context.SaveChangesAsync();
        }

        AuthenticateAsRole(UserRole.Server);
        var response = await PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = "DineIn",
            tableId,
            paymentState = "PayLater",
            items = new[] { new { productId = _productId, quantity = 1 } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Alphanumeric_session_open_can_receive_a_staff_round()
    {
        var tableId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Tables.Add(new Table
            {
                Id = tableId,
                TableNumber = "T-QA",
                MaxGuests = 4,
                IsActive = true,
                CreatedBy = "test"
            });
            await context.SaveChangesAsync();
        }

        AuthenticateAsRole(UserRole.Server);
        var opened = await PostAsJsonAsync("/api/table-service-sessions", new { tableId });
        var openedBody = (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(opened))!;
        openedBody.Success.Should().BeTrue();

        var created = await PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = "DineIn",
            tableId,
            serviceSessionId = openedBody.Data!.ServiceSessionId,
            paymentState = "PayLater",
            items = new[] { new { productId = _productId, quantity = 1 } }
        });
        var createdBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(created))!;

        createdBody.Success.Should().BeTrue();
        createdBody.Data!.TableId.Should().Be(tableId);
        createdBody.Data.TableNumber.Should().BeNull();
        createdBody.Data.TableLabel.Should().Be("T-QA");
        createdBody.Data.ServiceSessionId.Should().Be(openedBody.Data.ServiceSessionId);
    }

    [Fact]
    public async Task Dine_in_create_rejects_a_closed_session()
    {
        var tableId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Tables.Add(new Table
            {
                Id = tableId,
                TableNumber = "44",
                MaxGuests = 4,
                IsActive = true,
                CreatedBy = "test"
            });
            context.TableServiceSessions.Add(new TableServiceSession
            {
                Id = sessionId,
                TableId = tableId,
                TableNumber = 44,
                Status = TableServiceSessionStatus.Closed,
                Version = 2,
                OpenedAt = DateTime.UtcNow.AddHours(-1),
                ClosedAt = DateTime.UtcNow,
                CreatedBy = "test"
            });
            await context.SaveChangesAsync();
        }

        AuthenticateAsRole(UserRole.Server);
        var response = await PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = "DineIn",
            tableId,
            serviceSessionId = sessionId,
            paymentState = "PayLater",
            items = new[] { new { productId = _productId, quantity = 1 } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DineIn_create_assigns_only_the_matching_open_service_session()
    {
        var sessionId = Guid.NewGuid();
        await using (var context = DatabaseFixture.CreateContext())
        {
            context.Tables.Add(new Table
            {
                Id = Guid.NewGuid(),
                TableNumber = "9",
                MaxGuests = 4,
                IsActive = true,
                CreatedBy = "test"
            });
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
    public async Task Delivery_create_without_an_address_is_refused_with_the_street_message()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = "Delivery",
            paymentState = "PayLater",
            items = new[] { new { productId = _productId, quantity = 1 } }
        });
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Success.Should().BeFalse();
        body.Errors.Should().Contain(
            "A delivery counter order requires a delivery address with a street.");
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Delivery_create_with_an_explicit_address_persists_it_on_the_order()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = "Delivery",
            paymentState = "PayLater",
            deliveryAddress = new
            {
                addressLine1 = "Rue du Grand-Pré 45",
                city = "Genève",
                postalCode = "1202",
                country = "Switzerland"
            },
            items = new[] { new { productId = _productId, quantity = 1 } }
        });
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;

        body.Success.Should().BeTrue();
        body.Data!.DeliveryAddress!.AddressLine1.Should().Be("Rue du Grand-Pré 45");

        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.Include(value => value.DeliveryAddress).SingleAsync();
        order.Type.Should().Be(OrderType.Delivery);
        order.DeliveryAddress.Should().NotBeNull();
        order.DeliveryAddress!.AddressLine1.Should().Be("Rue du Grand-Pré 45");
        order.DeliveryAddress.City.Should().Be("Genève");
        (await context.StaffOrderOperations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Takeaway_create_carrying_a_delivery_address_is_refused()
    {
        AuthenticateAsAdmin();
        var response = await PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = false,
            type = "Takeaway",
            paymentState = "PayLater",
            deliveryAddress = new
            {
                addressLine1 = "Rue du Grand-Pré 45",
                city = "Genève",
                postalCode = "1202",
                country = "Switzerland"
            },
            items = new[] { new { productId = _productId, quantity = 1 } }
        });
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Success.Should().BeFalse();
        body.Errors.Should().Contain("A delivery address is valid only for delivery orders.");
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(UserRole.Server)]
    [InlineData(UserRole.Cashier)]
    public async Task Customer_association_is_authorized_and_server_authored(UserRole role)
    {
        AuthenticateAsRole(role);
        var accepted = await PostAsJsonAsync("/api/staff/orders", CreateBody(
            Guid.NewGuid(), false, _customerId));
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(accepted))!;
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Data!.UserId.Should().Be(_customerId);
        body.Data.CustomerName.Should().Be("Test User");
    }

    [Fact]
    public async Task Generic_status_update_cannot_release_a_held_order()
    {
        AuthenticateAsAdmin();
        var create = await PostAsJsonAsync("/api/staff/orders", CreateBody(Guid.NewGuid(), false));
        var created = (await ReadResponseAsync<ApiResponse<OrderDto>>(create))!.Data!;

        var response = await Client.PutAsJsonAsync($"/api/orders/{created.Id}/status", new
        {
            newStatus = "Confirmed",
            expectedVersion = created.Version
        });
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;

        body.Success.Should().BeFalse();
        body.ErrorCode.Should().Be(ErrorCodes.KitchenReleaseRequired);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders
            .Include(value => value.StatusHistory)
            .SingleAsync(value => value.Id == created.Id);
        order.IsKitchenReleased.Should().BeFalse();
        order.Status.Should().Be(OrderStatus.Pending);
        order.Version.Should().Be(created.Version);
        order.StatusHistory.Should().ContainSingle();
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
        Guid operationId, bool releaseToKitchen, Guid? customerId = null, int? pointsToRedeem = null,
        string? notes = null) => new
        {
            clientOperationId = operationId,
            releaseToKitchen,
            type = "Takeaway",
            customerUserId = customerId,
            pointsToRedeem,
            notes,
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

    private async Task SeedPointsAsync(int points)
    {
        await using var context = DatabaseFixture.CreateContext();
        context.FidelityPointBalances.Add(new FidelityPointBalance
        {
            UserId = _customerId,
            CurrentPoints = points,
            TotalEarnedPoints = points,
            LastUpdated = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        await context.SaveChangesAsync();
    }
}
