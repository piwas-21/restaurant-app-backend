using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed class ClearHeldStaffOrderTests : IntegrationTestBase
{
    private Guid _productId;
    private Guid _tableId;

    public ClearHeldStaffOrderTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Cashier_can_clear_held_staff_round_without_dispatch_and_audits_confirmed_source_status()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var order = await CreateRoundAsync(session.ServiceSessionId, releaseToKitchen: false);
        order.Status.Should().Be(nameof(OrderStatus.Confirmed));
        order.IsKitchenReleased.Should().BeFalse();

        await using (var before = DatabaseFixture.CreateContext())
        {
            (await before.OrderRoutingStates.CountAsync(value => value.OrderId == order.Id)).Should().Be(0);
        }

        AuthenticateAsRole(UserRole.Cashier);
        var currentSession = await ReadSessionAsync(session.ServiceSessionId);
        var response = await PostAsJsonAsync(
            $"/api/table-service-sessions/{session.ServiceSessionId}/clear-pending-orders",
            new { expectedVersion = currentSession.Version });
        var body = (await ReadResponseAsync<ApiResponse<ClearedTableOrdersDto>>(response))!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Success.Should().BeTrue();
        body.Data!.CancelledOrderCount.Should().Be(1);
        await using var verify = DatabaseFixture.CreateContext();
        var retained = await verify.Orders.Include(value => value.StatusHistory)
            .Include(value => value.Payments).Include(value => value.RoutingStates)
            .SingleAsync(value => value.Id == order.Id);
        retained.Status.Should().Be(OrderStatus.Cancelled);
        retained.PaymentStatus.Should().Be(PaymentStatus.Pending);
        retained.IsKitchenReleased.Should().BeFalse();
        retained.Payments.Should().BeEmpty();
        retained.RoutingStates.Should().BeEmpty();
        retained.StatusHistory.Should().Contain(history =>
            history.FromStatus == OrderStatus.Confirmed && history.ToStatus == OrderStatus.Cancelled);
        (await verify.TableServiceSessions.SingleAsync(value => value.Id == session.ServiceSessionId))
            .ReleasedAt.Should().NotBeNull();
        (await verify.Tables.SingleAsync(value => value.Id == _tableId)).ReadinessState
            .Should().Be(TableReadinessState.NeedsReset);

        var routing = await Client.GetFromJsonAsync<ApiResponse<List<OrderRoutingStateDto>>>(
            $"/api/staff/orders/{order.Id}/routing", JsonOptions);
        routing!.Success.Should().BeTrue();
        routing.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Legacy_confirmed_held_order_can_be_cleared_without_losing_its_row_or_history()
    {
        var orderId = await SeedLegacyHeldOrderAsync();
        AuthenticateAsRole(UserRole.Cashier);

        var response = await PostAsJsonAsync("/api/table-service-sessions/legacy/7/clear-pending-orders", new { });
        var body = (await ReadResponseAsync<ApiResponse<ClearedTableOrdersDto>>(response))!;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Success.Should().BeTrue();
        body.Data!.CancelledOrderCount.Should().Be(1);
        await using var verify = DatabaseFixture.CreateContext();
        var retained = await verify.Orders.Include(value => value.StatusHistory)
            .SingleAsync(value => value.Id == orderId);
        retained.Status.Should().Be(OrderStatus.Cancelled);
        retained.ServiceSessionId.Should().BeNull();
        retained.TableNumber.Should().Be(7);
        retained.StatusHistory.Should().Contain(history =>
            history.FromStatus == OrderStatus.Confirmed && history.ToStatus == OrderStatus.Cancelled);
        (await verify.Tables.SingleAsync(value => value.Id == _tableId)).ReadinessState
            .Should().Be(TableReadinessState.NeedsReset);
    }

    [Theory]
    [InlineData("released")]
    [InlineData("paid")]
    [InlineData("preparing")]
    [InlineData("handoff")]
    public async Task Clear_refuses_released_paid_preparing_or_handoff_orders_without_mutation(string scenario)
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var order = await CreateRoundAsync(session.ServiceSessionId, releaseToKitchen: scenario is "released" or "preparing");
        var expectedOrderStatus = OrderStatus.Confirmed;

        switch (scenario)
        {
            case "released":
                order.IsKitchenReleased.Should().BeTrue();
                break;
            case "paid":
                AuthenticateAsRole(UserRole.Cashier);
                var beforePayment = await ReadSessionAsync(session.ServiceSessionId);
                var payment = await PostAsJsonAsync($"/api/table-service-sessions/{session.ServiceSessionId}/payments", new
                {
                    operationId = Guid.NewGuid(),
                    expectedVersion = beforePayment.Version,
                    paymentMethod = nameof(PaymentMethod.Cash),
                    amount = order.Total,
                    currency = beforePayment.Currency
                });
                (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(payment))!.Success.Should().BeTrue();
                break;
            case "preparing":
                AuthenticateAsAdmin();
                var preparing = await PutAsJsonAsync($"/api/Orders/{order.Id}/status", new
                {
                    newStatus = nameof(OrderStatus.Preparing),
                    expectedVersion = order.Version
                });
                (await ReadResponseAsync<ApiResponse<OrderDto>>(preparing))!.Success.Should().BeTrue();
                expectedOrderStatus = OrderStatus.Preparing;
                break;
            case "handoff":
                var beforeHandoff = await ReadSessionAsync(session.ServiceSessionId);
                var handoff = await PostAsJsonAsync(
                    $"/api/table-service-sessions/{session.ServiceSessionId}/payment-handoff", new
                    {
                        operationId = Guid.NewGuid(),
                        expectedVersion = beforeHandoff.Version
                    });
                (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(handoff))!.Success.Should().BeTrue();
                break;
        }

        AuthenticateAsRole(UserRole.Cashier);
        var latestSession = await ReadSessionAsync(session.ServiceSessionId);
        var clear = await PostAsJsonAsync(
            $"/api/table-service-sessions/{session.ServiceSessionId}/clear-pending-orders",
            new { expectedVersion = latestSession.Version });
        var result = (await ReadResponseAsync<ApiResponse<ClearedTableOrdersDto>>(clear))!;

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.TableServiceSessionNotClosable);
        await using var verify = DatabaseFixture.CreateContext();
        var retained = await verify.Orders.Include(value => value.StatusHistory)
            .Include(value => value.Payments).Include(value => value.RoutingStates)
            .SingleAsync(value => value.Id == order.Id);
        retained.Status.Should().Be(expectedOrderStatus);
        retained.StatusHistory.Should().NotContain(history => history.ToStatus == OrderStatus.Cancelled);
        retained.IsKitchenReleased.Should().Be(scenario is "released" or "preparing");
        retained.Payments.Should().HaveCount(scenario == "paid" ? 1 : 0);
        if (scenario is "released" or "preparing")
        {
            retained.RoutingStates.Should().NotBeEmpty();
        }
        else
        {
            retained.RoutingStates.Should().BeEmpty();
        }
        var unchangedSession = await verify.TableServiceSessions.SingleAsync(value => value.Id == session.ServiceSessionId);
        unchangedSession.Status.Should().Be(TableServiceSessionStatus.Open);
        unchangedSession.ReleasedAt.Should().BeNull();
        unchangedSession.Version.Should().Be(latestSession.Version);
        (await verify.TableServicePaymentHandoffs.CountAsync(value =>
            value.ServiceSessionId == session.ServiceSessionId
            && value.Status == TableServicePaymentHandoffStatus.Requested)).Should().Be(scenario == "handoff" ? 1 : 0);
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _productId = await context.Products.Where(value => value.Name == "Test Pizza")
            .Select(value => value.Id).SingleAsync();
        _tableId = Guid.NewGuid();
        context.Tables.Add(new Table
        {
            Id = _tableId,
            TableNumber = "7",
            MaxGuests = 4,
            IsActive = true,
            ReadinessState = TableReadinessState.ReadyForGuests,
            CreatedBy = nameof(ClearHeldStaffOrderTests)
        });
        await context.SaveChangesAsync();
    }

    private async Task<TableServiceSessionDto> OpenSessionAsync()
    {
        var response = await PostAsJsonAsync("/api/table-service-sessions", new { tableId = _tableId });
        var body = (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(response))!;
        body.Success.Should().BeTrue();
        return body.Data!;
    }

    private async Task<OrderDto> CreateRoundAsync(Guid sessionId, bool releaseToKitchen)
    {
        var response = await PostAsJsonAsync("/api/staff/orders/round", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen,
            type = nameof(OrderType.DineIn),
            tableId = _tableId,
            serviceSessionId = sessionId,
            paymentState = "Unpaid",
            items = new[] { new { productId = _productId, quantity = 1 } }
        });
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;
        body.Success.Should().BeTrue();
        return body.Data!;
    }

    private async Task<TableServiceSessionDto> ReadSessionAsync(Guid sessionId)
    {
        var response = await Client.GetAsync($"/api/table-service-sessions/{sessionId}");
        var body = (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(response))!;
        body.Success.Should().BeTrue();
        return body.Data!;
    }

    private async Task<Guid> SeedLegacyHeldOrderAsync()
    {
        await using var context = DatabaseFixture.CreateContext();
        var now = DateTime.UtcNow;
        var orderId = Guid.NewGuid();
        context.Orders.Add(new Order
        {
            Id = orderId,
            OrderNumber = $"LEG-{Guid.NewGuid():N}"[..20],
            Type = OrderType.DineIn,
            TableNumber = 7,
            SubTotal = 12.99m,
            Total = 12.99m,
            RemainingAmount = 12.99m,
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Pending,
            OrderDate = now,
            IsKitchenReleased = false,
            CreatedAt = now,
            CreatedBy = nameof(ClearHeldStaffOrderTests),
            Items =
            [
                new OrderItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = _productId,
                    ProductName = "Test Pizza",
                    Quantity = 1,
                    UnitPrice = 12.99m,
                    ItemTotal = 12.99m,
                    CreatedAt = now,
                    CreatedBy = nameof(ClearHeldStaffOrderTests)
                }
            ],
            StatusHistory =
            [
                new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    FromStatus = OrderStatus.Pending,
                    ToStatus = OrderStatus.Confirmed,
                    Notes = "Legacy staff round held before kitchen release",
                    ChangedAt = now,
                    ChangedBy = nameof(ClearHeldStaffOrderTests),
                    CreatedAt = now,
                    CreatedBy = nameof(ClearHeldStaffOrderTests)
                }
            ]
        });
        await context.SaveChangesAsync();
        return orderId;
    }
}
