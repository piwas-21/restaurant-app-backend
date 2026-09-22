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
public sealed class StaffRoundOrderTests : IntegrationTestBase
{
    private Guid _tableId;
    private Guid _productId;

    public StaffRoundOrderTests(DatabaseFixture databaseFixture) : base(databaseFixture)
    {
    }

    [Fact]
    public async Task Held_round_is_attached_to_session_without_routes_and_replays_by_actor()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var operationId = Guid.NewGuid();

        var first = await PostAsJsonAsync("/api/staff/orders/round", Body(
            session.ServiceSessionId, operationId, releaseToKitchen: false));
        var firstBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(first))!;

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        firstBody.Success.Should().BeTrue();
        firstBody.Data!.ServiceSessionId.Should().Be(session.ServiceSessionId);
        firstBody.Data.Type.Should().Be(nameof(OrderType.DineIn));
        firstBody.Data.IsKitchenReleased.Should().BeFalse();
        firstBody.Data.RoutingStates.Should().BeNull();

        var retry = await PostAsJsonAsync("/api/staff/orders/round", Body(
            session.ServiceSessionId, operationId, releaseToKitchen: false));
        var retryBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(retry))!;
        retryBody.Data!.Id.Should().Be(firstBody.Data.Id);

        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.OrderRoutingStates.CountAsync()).Should().Be(0);
        (await context.StaffOrderOperations.SingleAsync()).Kind
            .Should().Be(StaffOrderOperationKind.RoundCreate);
    }

    [Fact]
    public async Task Released_round_creates_durable_required_routes_before_response()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var response = await PostAsJsonAsync("/api/staff/orders/round", Body(
            session.ServiceSessionId, Guid.NewGuid(), releaseToKitchen: true));
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;

        body.Success.Should().BeTrue();
        body.Data!.RoutingStates.Should().NotBeNullOrEmpty();
        body.Data.RoutingStates!.Should().Contain(route => route.IsRequired);

        await using var context = DatabaseFixture.CreateContext();
        var orderId = body.Data.Id;
        (await context.OrderRoutingStates.CountAsync(route => route.OrderId == orderId))
            .Should().Be(body.Data.RoutingStates.Count);
    }

    [Fact]
    public async Task Round_rejects_takeaway_and_delivery_address_without_mutation()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var response = await PostAsJsonAsync("/api/staff/orders/round", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = true,
            type = "Takeaway",
            serviceSessionId = session.ServiceSessionId,
            deliveryAddress = new
            {
                addressLine1 = "Rue de la Table 1",
                city = "Geneva",
                postalCode = "1200",
                country = "Switzerland"
            },
            items = new[] { new { productId = _productId, quantity = 1 } }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(0);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Foreign_actor_cannot_reconcile_round_operation()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var operationId = Guid.NewGuid();
        var create = await PostAsJsonAsync("/api/staff/orders/round", Body(
            session.ServiceSessionId, operationId, releaseToKitchen: false));
        (await ReadResponseAsync<ApiResponse<OrderDto>>(create))!.Success.Should().BeTrue();

        AuthenticateAsAdmin();
        var lookup = await Client.GetAsync($"/api/staff/orders/round/operations/{operationId}");
        var lookupBody = (await ReadResponseAsync<ApiResponse<StaffRoundOperationLookupDto>>(lookup))!;

        lookup.StatusCode.Should().Be(HttpStatusCode.OK);
        lookupBody.Success.Should().BeTrue();
        lookupBody.Data!.Status.Should().Be(StaffRoundOperationLookupStatus.Unknown);
        lookupBody.Data.Order.Should().BeNull();
    }

    [Fact]
    public async Task Own_actor_operation_lookup_returns_the_committed_round()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var operationId = Guid.NewGuid();
        var create = await PostAsJsonAsync("/api/staff/orders/round", Body(
            session.ServiceSessionId, operationId, releaseToKitchen: false));
        var created = (await ReadResponseAsync<ApiResponse<OrderDto>>(create))!;

        var lookup = await Client.GetAsync($"/api/staff/orders/round/operations/{operationId}");
        var lookupBody = (await ReadResponseAsync<ApiResponse<StaffRoundOperationLookupDto>>(lookup))!;

        lookupBody.Success.Should().BeTrue();
        lookupBody.Data!.Status.Should().Be(StaffRoundOperationLookupStatus.Committed);
        lookupBody.Data.Order!.Id.Should().Be(created.Data!.Id);
    }

    [Fact]
    public async Task Concurrent_duplicate_client_operation_id_creates_one_round_and_operation()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var operationId = Guid.NewGuid();
        var requests = Enumerable.Range(0, 2).Select(_ =>
            PostAsJsonAsync("/api/staff/orders/round", Body(
                session.ServiceSessionId, operationId, releaseToKitchen: true))).ToArray();

        var responses = await Task.WhenAll(requests);
        var bodies = await Task.WhenAll(responses.Select(ReadOrderAsync));

        bodies.Should().OnlyContain(body => body.Success);
        bodies.Select(body => body.Data!.Id).Should()
            .OnlyContain(id => id == bodies[0].Data!.Id);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync()).Should().Be(1);
        (await context.StaffOrderOperations.CountAsync()).Should().Be(1);
        (await context.OrderRoutingStates.CountAsync()).Should().Be(bodies[0].Data!.RoutingStates!.Count);
    }

    [Fact]
    public async Task Held_round_release_replay_creates_routes_once()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var create = await PostAsJsonAsync("/api/staff/orders/round", Body(
            session.ServiceSessionId, Guid.NewGuid(), releaseToKitchen: false));
        var created = (await ReadResponseAsync<ApiResponse<OrderDto>>(create))!.Data!;
        var operationId = Guid.NewGuid();

        var first = await PostAsJsonAsync($"/api/staff/orders/{created.Id}/release", new
        {
            clientOperationId = operationId,
            expectedVersion = created.Version
        });
        var retry = await PostAsJsonAsync($"/api/staff/orders/{created.Id}/release", new
        {
            clientOperationId = operationId,
            expectedVersion = created.Version
        });
        var firstBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(first))!;
        var retryBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(retry))!;

        firstBody.Success.Should().BeTrue();
        retryBody.Success.Should().BeTrue();
        retryBody.Data!.Id.Should().Be(firstBody.Data!.Id);
        await using var context = DatabaseFixture.CreateContext();
        (await context.OrderRoutingStates.CountAsync(route => route.OrderId == created.Id))
            .Should().Be(firstBody.Data.RoutingStates!.Count);
    }

    [Fact]
    public async Task Server_cannot_transition_a_released_order_into_preparing()
    {
        AuthenticateAsRole(UserRole.Server);
        var created = await CreateReleasedTakeawayAsync();

        var response = await PutAsJsonAsync($"/api/Orders/{created.Id}/status", new
        {
            newStatus = nameof(OrderStatus.Preparing),
            expectedVersion = created.Version
        });
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;

        body.Success.Should().BeFalse();
        body.ErrorCode.Should().Be(ErrorCodes.KitchenRoleRequired);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(order => order.Id == created.Id)).Status
            .Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task Server_cannot_record_a_payment_for_a_takeaway()
    {
        AuthenticateAsRole(UserRole.Server);
        var created = await CreateReleasedTakeawayAsync();

        var response = await PostAsJsonAsync($"/api/Orders/{created.Id}/payments", new
        {
            operationId = Guid.NewGuid(),
            paymentMethod = nameof(PaymentMethod.Cash),
            amount = created.Total
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using var context = DatabaseFixture.CreateContext();
        (await context.OrderPayments.CountAsync(payment => payment.OrderId == created.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Server_cannot_cancel_after_kitchen_release()
    {
        AuthenticateAsRole(UserRole.Server);
        var created = await CreateReleasedTakeawayAsync();

        var response = await PostAsJsonAsync($"/api/Orders/{created.Id}/cancel", new
        {
            cancellationReason = "Server cancellation attempt",
            expectedVersion = created.Version
        });
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;

        body.Success.Should().BeFalse();
        body.ErrorCode.Should().Be(ErrorCodes.KitchenRoleRequired);
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.SingleAsync(order => order.Id == created.Id)).Status
            .Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task Server_can_complete_a_ready_takeaway_handoff()
    {
        AuthenticateAsRole(UserRole.Server);
        var created = await CreateReleasedTakeawayAsync();

        AuthenticateAsAdmin();
        var preparing = await PutAsJsonAsync($"/api/Orders/{created.Id}/status", new
        {
            newStatus = nameof(OrderStatus.Preparing),
            expectedVersion = created.Version
        });
        var preparingBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(preparing))!;
        var ready = await PutAsJsonAsync($"/api/Orders/{created.Id}/status", new
        {
            newStatus = nameof(OrderStatus.Ready),
            expectedVersion = preparingBody.Data!.Version
        });
        var readyBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(ready))!;

        AuthenticateAsRole(UserRole.Server);
        var handoff = await PutAsJsonAsync($"/api/Orders/{created.Id}/status", new
        {
            newStatus = nameof(OrderStatus.Completed),
            expectedVersion = readyBody.Data!.Version
        });
        var handoffBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(handoff))!;

        handoffBody.Success.Should().BeTrue();
        handoffBody.Data!.Status.Should().Be(nameof(OrderStatus.Completed));
    }

    [Fact]
    public async Task Concurrent_rounds_serialize_on_the_session_and_both_memberships_are_durable()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var requests = Enumerable.Range(0, 2).Select(index =>
            PostAsJsonAsync("/api/staff/orders/round", Body(
                session.ServiceSessionId, Guid.NewGuid(), releaseToKitchen: true))).ToArray();

        var responses = await Task.WhenAll(requests);
        var bodies = await Task.WhenAll(responses.Select(ReadOrderAsync));

        bodies.Should().OnlyContain(body => body.Success);
        bodies.Select(body => body.Data!.Id).Should().OnlyHaveUniqueItems();
        await using var context = DatabaseFixture.CreateContext();
        (await context.Orders.CountAsync(order => order.ServiceSessionId == session.ServiceSessionId))
            .Should().Be(2);
    }

    [Fact]
    public async Task Round_creation_and_close_cannot_both_commit_against_one_session()
    {
        AuthenticateAsRole(UserRole.Server);
        var session = await OpenSessionAsync();
        var createTask = PostAsJsonAsync("/api/staff/orders/round", Body(
            session.ServiceSessionId, Guid.NewGuid(), releaseToKitchen: false));
        var closeTask = PostAsJsonAsync(
            $"/api/table-service-sessions/{session.ServiceSessionId}/close",
            new { expectedVersion = session.Version });

        var responses = await Task.WhenAll(createTask, closeTask);
        var createBody = (await ReadResponseAsync<ApiResponse<OrderDto>>(responses[0]))!;
        var closeBody = (await ReadResponseAsync<ApiResponse<TableServiceSessionDto>>(responses[1]))!;

        (createBody.Success && closeBody.Success).Should().BeFalse();
        await using var context = DatabaseFixture.CreateContext();
        var persisted = await context.TableServiceSessions.SingleAsync(
            value => value.Id == session.ServiceSessionId);
        if (createBody.Success)
        {
            persisted.Status.Should().Be(TableServiceSessionStatus.Open);
            (await context.Orders.CountAsync(order => order.ServiceSessionId == session.ServiceSessionId))
                .Should().Be(1);
        }
        else
        {
            persisted.Status.Should().Be(TableServiceSessionStatus.Closed);
            (await context.Orders.CountAsync(order => order.ServiceSessionId == session.ServiceSessionId))
                .Should().Be(0);
        }
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        await using var context = DatabaseFixture.CreateContext();
        _productId = await context.Products
            .Where(product => product.Name == "Test Pizza")
            .Select(product => product.Id)
            .SingleAsync();
        _tableId = Guid.NewGuid();
        context.Tables.Add(new Table
        {
            Id = _tableId,
            TableNumber = "7",
            MaxGuests = 4,
            IsActive = true,
            CreatedBy = nameof(StaffRoundOrderTests)
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

    private object Body(Guid sessionId, Guid operationId, bool releaseToKitchen) => new
    {
        clientOperationId = operationId,
        releaseToKitchen,
        type = "DineIn",
        tableId = _tableId,
        serviceSessionId = sessionId,
        paymentState = "Unpaid",
        notes = "round test",
        items = new[] { new { productId = _productId, quantity = 1 } }
    };

    private async Task<OrderDto> CreateReleasedTakeawayAsync()
    {
        var response = await PostAsJsonAsync("/api/staff/orders", new
        {
            clientOperationId = Guid.NewGuid(),
            releaseToKitchen = true,
            type = nameof(OrderType.Takeaway),
            paymentState = nameof(StaffOrderPaymentState.Unpaid),
            items = new[] { new { productId = _productId, quantity = 1 } }
        });
        var body = (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;
        body.Success.Should().BeTrue();
        return body.Data!;
    }

    private async Task<ApiResponse<OrderDto>> ReadOrderAsync(HttpResponseMessage response)
    {
        return (await ReadResponseAsync<ApiResponse<OrderDto>>(response))!;
    }
}
