using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 3")]
public sealed partial class AccountPaymentStaffCollectionEndpointTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("Server", false, "server", HttpStatusCode.NotFound)]
    [InlineData("Server", true, "server", HttpStatusCode.OK)]
    [InlineData("Server", true, "cashier", HttpStatusCode.NotFound)]
    [InlineData("Cashier", false, "server", HttpStatusCode.OK)]
    [InlineData("Cashier", false, "cashier", HttpStatusCode.OK)]
    [InlineData("Admin", false, "cashier", HttpStatusCode.OK)]
    [InlineData("Admin", true, "core", HttpStatusCode.NotFound)]
    [InlineData("Cashier", true, "core", HttpStatusCode.NotFound)]
    [InlineData("Server", true, "core", HttpStatusCode.NotFound)]
    [InlineData("KitchenStaff", true, "server", HttpStatusCode.Forbidden)]
    [InlineData("Customer", true, "server", HttpStatusCode.Forbidden)]
    public async Task New_collection_endpoints_enforce_role_module_and_server_opt_in_before_persisting(
        string role, bool optIn, string module, HttpStatusCode expected)
    {
        var account = await Seed();
        using var factory = Factory(payments: true, optIn, module);
        using var client = Client(factory, role);
        var request = Quote(account);
        var response = await client.PostAsJsonAsync($"{Route(account.SessionId)}/quotes", request);
        response.StatusCode.Should().Be(expected);
        (await client.GetAsync(Route(account.SessionId))).StatusCode.Should().Be(expected);
        var planRequest = new CreateAccountEqualSharePlanRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 1,
            ShareCount = 3
        };
        var planResponse = await client.PostAsJsonAsync($"{Route(account.SessionId)}/equal-share-plans", planRequest);
        planResponse.StatusCode.Should().Be(expected);
        await using var context = fixture.CreateContext();
        (await context.AccountPaymentAttempts.CountAsync()).Should().Be(expected == HttpStatusCode.OK ? 1 : 0);
        (await context.AccountEqualSharePlans.CountAsync()).Should().Be(expected == HttpStatusCode.OK ? 1 : 0);
        if (expected == HttpStatusCode.OK)
        {
            var body = await response.Content.ReadFromJsonAsync<ApiResponse<AccountPaymentOperationDto>>(JsonOptions);
            body!.Data!.AmountMinor.Should().Be(1000);
            body.Data.State.Should().Be(AccountPaymentState.Quoted);
            var bill = await client.GetFromJsonAsync<ApiResponse<AccountPaymentAccountDto>>(Route(account.SessionId), JsonOptions);
            bill!.Data!.AvailableMinor.Should().Be(1000);
            bill.Data.ActiveEqualSharePlan!.Slots.Select(slot => slot.AmountMinor)
                .Should().Equal(334L, 333L, 333L);
        }
    }

    [Fact]
    public async Task Server_can_reserve_and_capture_a_reviewed_equal_share_slot()
    {
        var account = await Seed();
        using var factory = Factory(payments: true, optIn: true);
        using var client = Client(factory, "Server");
        var route = Route(account.SessionId);
        var planRequest = new CreateAccountEqualSharePlanRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 1,
            ShareCount = 3
        };

        using var planResponse = await client.PostAsJsonAsync($"{route}/equal-share-plans", planRequest);
        planResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = (await planResponse.Content.ReadFromJsonAsync<
            ApiResponse<AccountEqualSharePlanDto>>(JsonOptions))!.Data!;
        plan.TotalMinor.Should().Be(1000);
        plan.Scope.Should().ContainSingle(value => value.OrderId == account.OrderId
            && value.OrderItemId == account.ItemId && value.AmountMinor == 1000);

        var quoteRequest = new CreateAccountPaymentQuoteRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 1,
            Mode = AccountPaymentMode.Equal,
            PaymentMethod = PaymentMethod.CreditCard,
            EqualSharePlanId = plan.PlanId,
            EqualShareOrdinal = 1
        };
        using var quoteResponse = await client.PostAsJsonAsync($"{route}/quotes", quoteRequest);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<AccountPaymentOperationDto>>(JsonOptions))!.Data!;
        quote.State.Should().Be(AccountPaymentState.Quoted);
        quote.AmountMinor.Should().Be(334);
        quote.EqualSharePlanId.Should().Be(plan.PlanId);
        quote.EqualShareOrdinal.Should().Be(1);

        var operationRoute = $"{route}/operations/{quote.OperationId}";
        using var reserveResponse = await client.PostAsJsonAsync($"{operationRoute}/reserve",
            new ReserveAccountPaymentRequest { ExpectedVersion = 1, ExpectedAccountRevision = 1 });
        reserveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reserved = (await reserveResponse.Content.ReadFromJsonAsync<
            ApiResponse<AccountPaymentOperationDto>>(JsonOptions))!.Data!;
        reserved.State.Should().Be(AccountPaymentState.Reserved);
        reserved.Version.Should().Be(2);

        var reservedAccount = (await client.GetFromJsonAsync<
            ApiResponse<AccountPaymentAccountDto>>(route, JsonOptions))!.Data!;
        var reservedPlan = reservedAccount.ActiveEqualSharePlan!;
        reservedPlan.Slots[0].ClaimState.Should().Be(AccountPaymentState.Reserved);
        reservedPlan.Slots[0].IsAvailable.Should().BeFalse();

        using var captureResponse = await client.PostAsJsonAsync($"{operationRoute}/collect",
            new CaptureAccountPaymentRequest { ExpectedVersion = 2 });
        captureResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var captured = (await captureResponse.Content.ReadFromJsonAsync<
            ApiResponse<AccountPaymentOperationDto>>(JsonOptions))!.Data!;
        captured.State.Should().Be(AccountPaymentState.Captured);
        captured.Version.Should().Be(3);

        var finalAccount = (await client.GetFromJsonAsync<
            ApiResponse<AccountPaymentAccountDto>>(route, JsonOptions))!.Data!;
        finalAccount.OutstandingMinor.Should().Be(666);
        finalAccount.AvailableMinor.Should().Be(666);
        finalAccount.CapturedAccountPaymentMinor.Should().Be(334);
        var capturedPlan = finalAccount.ActiveEqualSharePlan!;
        capturedPlan.Slots.Select(value => value.AmountMinor).Should().Equal(334L, 333L, 333L);
        capturedPlan.Slots[0].ClaimState.Should().Be(AccountPaymentState.Captured);

        await using var verify = fixture.CreateContext();
        var attempt = await verify.AccountPaymentAttempts.SingleAsync(value => value.OperationId == quote.OperationId);
        attempt.State.Should().Be(AccountPaymentState.Captured);
        attempt.EqualSharePlanId.Should().Be(plan.PlanId);
        attempt.EqualShareOrdinal.Should().Be(1);
        var order = await verify.Orders.Include(value => value.Payments)
            .SingleAsync(value => value.Id == account.OrderId);
        order.TotalPaid.Should().Be(3.34m);
        order.RemainingAmount.Should().Be(6.66m);
        order.Payments.Should().ContainSingle(value => value.Amount == 3.34m
            && value.PaymentMethod == PaymentMethod.CreditCard
            && value.Status == PaymentStatus.Completed);
    }

    [Fact]
    public async Task Server_owner_can_release_after_opt_out_without_leaking_operation_to_other_actor_or_visit()
    {
        var account = await Seed();
        var otherVisit = await Seed();
        using var enabledFactory = Factory(payments: true, optIn: true);
        using var enabled = Client(enabledFactory, "Server");
        var request = Quote(account);
        (await enabled.PostAsJsonAsync($"{Route(account.SessionId)}/quotes", request))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var operationRoute = $"{Route(account.SessionId)}/operations/{request.OperationId}";
        var reserve = new ReserveAccountPaymentRequest { ExpectedVersion = 1, ExpectedAccountRevision = 1 };
        (await enabled.PostAsJsonAsync($"{operationRoute}/reserve", reserve)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var disabledFactory = Factory(payments: false, optIn: false, module: "core");
        using var owner = Client(disabledFactory, "Server");
        using var other = Client(disabledFactory, "Admin");
        var release = new ReleaseAccountPaymentRequest { ExpectedVersion = 2 };
        (await other.PostAsJsonAsync($"{operationRoute}/release", release)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var wrongVisitRoute = $"{Route(otherVisit.SessionId)}/operations/{request.OperationId}";
        (await owner.GetAsync(wrongVisitRoute)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync($"{wrongVisitRoute}/release", release)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync($"{operationRoute}/release", release)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PostAsJsonAsync($"{operationRoute}/release", release)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var context = fixture.CreateContext();
        var attempt = await context.AccountPaymentAttempts.SingleAsync();
        attempt.State.Should().Be(AccountPaymentState.Released);
        attempt.Version.Should().Be(3);
        (await context.OrderPayments.CountAsync()).Should().Be(0);
        (await context.Orders.SumAsync(order => order.TotalPaid)).Should().Be(0m);
    }

    [Fact]
    public async Task Accepted_server_reservation_replays_and_collects_after_opt_out_but_quoted_work_cannot_reserve()
    {
        var account = await Seed();
        using var enabledFactory = Factory(payments: true, optIn: true);
        using var enabled = Client(enabledFactory, "Server");
        var original = Quote(account);
        var untouched = Quote(account);
        (await enabled.PostAsJsonAsync($"{Route(account.SessionId)}/quotes", original)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await enabled.PostAsJsonAsync($"{Route(account.SessionId)}/quotes", untouched)).StatusCode.Should().Be(HttpStatusCode.OK);
        var reserve = new ReserveAccountPaymentRequest { ExpectedVersion = 1, ExpectedAccountRevision = 1 };
        var operationRoute = $"{Route(account.SessionId)}/operations/{original.OperationId}";
        (await enabled.PostAsJsonAsync($"{operationRoute}/reserve", reserve)).StatusCode.Should().Be(HttpStatusCode.OK);
        var enabledSession = await enabled.GetFromJsonAsync<ApiResponse<TableServiceSessionDto>>($"/api/table-service-sessions/{account.SessionId}", JsonOptions);
        enabledSession!.Data!.CanCollect.Should().BeTrue();
        enabledSession.Data.CanRequestPaymentHandoff.Should().BeFalse();

        using var disabledFactory = Factory(payments: false, optIn: false);
        using var owner = Client(disabledFactory, "Server");
        (await owner.GetAsync(operationRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        var replayResponse = await owner.PostAsJsonAsync($"{operationRoute}/reserve", reserve);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = await replayResponse.Content.ReadFromJsonAsync<ApiResponse<AccountPaymentOperationDto>>(JsonOptions);
        replay!.Data!.Version.Should().Be(2);
        (await owner.PostAsJsonAsync($"{Route(account.SessionId)}/operations/{untouched.OperationId}/reserve", reserve))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync($"{Route(account.SessionId)}/quotes", Quote(account))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var disabledSession = await owner.GetFromJsonAsync<ApiResponse<TableServiceSessionDto>>($"/api/table-service-sessions/{account.SessionId}", JsonOptions);
        disabledSession!.Data!.CanCollect.Should().BeFalse();
        disabledSession.Data.CanRequestPaymentHandoff.Should().BeTrue();

        using var other = Client(disabledFactory, "Admin");
        (await other.GetAsync(operationRoute)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var collect = new CaptureAccountPaymentRequest { ExpectedVersion = 2 };
        (await other.PostAsJsonAsync($"{operationRoute}/collect", collect)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync($"{operationRoute}/collect", collect)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PostAsJsonAsync($"{operationRoute}/collect", collect)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var verify = fixture.CreateContext();
        (await verify.OrderPayments.CountAsync()).Should().Be(1);
        (await verify.OrderPayments.SingleAsync()).Amount.Should().Be(10m);
        (await verify.AccountPaymentAttempts.SingleAsync(value => value.OperationId == untouched.OperationId))
            .State.Should().Be(AccountPaymentState.Quoted);
        (await verify.Orders.SingleAsync(value => value.Id == account.OrderId)).TotalPaid.Should().Be(10m);
    }

    private TestWebApplicationFactory Factory(bool payments, bool optIn, string module = "server") =>
        new(fixture.ConnectionString, new Dictionary<string, string>
        {
            ["TenantFeatures:TableAccountPaymentsV1"] = payments.ToString(),
            ["TenantFeatures:ServerAccountCollectionV1"] = optIn.ToString(),
            ["Modules:Enforce"] = "true",
            ["Modules:Enabled"] = module
        });

    private static HttpClient Client(TestWebApplicationFactory factory, string role)
    {
        var client = factory.CreateClient();
        if (role == "Admin") client.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        else client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, role);
        return client;
    }

    private async Task<AccountIdentity> Seed()
    {
        var session = new TableServiceSession { Id = Guid.NewGuid(), Currency = "CHF", OpenedAt = DateTime.UtcNow, CreatedBy = "authority-test" };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"AUTH-{Guid.NewGuid():N}"[..15],
            ServiceSessionId = session.Id,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Pending,
            SubTotal = 10m,
            Total = 10m,
            RemainingAmount = 10m,
            OrderDate = DateTime.UtcNow,
            CreatedBy = "authority-test"
        };
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            ProductName = "Frozen authority test dish",
            Quantity = 1,
            UnitPrice = 10m,
            ItemTotal = 10m,
            CreatedBy = "authority-test"
        };
        order.Items.Add(item);
        await using var context = fixture.CreateContext();
        context.TableServiceSessions.Add(session);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return new(session.Id, order.Id, item.Id);
    }

    private static CreateAccountPaymentQuoteRequest Quote(AccountIdentity account) => new()
    {
        OperationId = Guid.NewGuid(),
        ExpectedAccountRevision = 1,
        Mode = AccountPaymentMode.Items,
        PaymentMethod = PaymentMethod.CreditCard,
        SelectedUnits = [new AccountPaymentUnitSelection(account.OrderId, account.ItemId, 1)]
    };

    private static string Route(Guid sessionId) => $"/api/table-service-sessions/{sessionId}/account-payments";
    private sealed record AccountIdentity(Guid SessionId, Guid OrderId, Guid ItemId);
}
