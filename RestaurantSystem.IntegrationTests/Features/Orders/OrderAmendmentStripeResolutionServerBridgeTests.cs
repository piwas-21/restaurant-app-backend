using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.ServerWorkspace.Dtos;
using RestaurantSystem.Api.Features.TableServiceSessions.Commands.CloseTableServiceSessionCommand;
using RestaurantSystem.Api.Features.TableServiceSessions.Dtos;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Fact]
    public async Task Resolved_paid_amendment_credit_is_excluded_from_server_collection_and_visit_closes()
    {
        var balance = await SeedServerBridgeBalanceAsync();
        AuthenticateAsAdmin();
        var resolutionPath = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        var resolutionQuote = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = _clientOperationId,
            ExpectedOrderVersion = 2,
            ExpectedAccountRevision = 1,
            Currency = "CHF"
        };
        using var quoteResponse = await Client.PostAsJsonAsync(
            $"{resolutionPath}/quote", resolutionQuote, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        quote.CreditMinor.Should().Be(1000);
        quote.RefundLegs.Should().ContainSingle(value => value.AmountMinor == 1000
            && value.Custody == "StripeDirect");

        var startRequest = new OrderAmendmentResolutionStartRequest
        {
            Quote = resolutionQuote,
            QuoteHash = quote.QuoteHash,
            ExpiresAt = quote.ExpiresAt
        };
        using var startResponse = await Client.PostAsJsonAsync(resolutionPath, startRequest, JsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var start = (await startResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        start.Outcome.Should().Be("accepted");
        start.Result!.State.Should().Be("ReconciliationRequired");

        _features.OrderAmendmentsV1 = false;
        using var replayResponse = await Client.PostAsJsonAsync(resolutionPath, startRequest, JsonOptions);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        replay.Outcome.Should().Be("accepted");
        replay.Result!.State.Should().Be("Resolved");

        using var serverFactory = new TestWebApplicationFactory(
            DatabaseFixture.ConnectionString,
            new Dictionary<string, string>
            {
                ["Modules:Enforce"] = "true",
                ["Modules:Enabled"] = "server"
            },
            services =>
            {
                services.RemoveAll<ITenantFeatures>();
                services.AddSingleton<ITenantFeatures>(new TenantFeatures(Options.Create(
                    new TenantFeatureSettings
                    {
                        TableAccountPaymentsV1 = true,
                        ServerAccountCollectionV1 = true
                    })));
            });
        using var server = serverFactory.CreateClient();
        server.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, UserRole.Server.ToString());

        var accountPath = $"/api/table-service-sessions/{_sessionId}/account-payments";
        var accountResponse = await server.GetFromJsonAsync<ApiResponse<AccountPaymentAccountDto>>(
            accountPath, JsonOptions);
        var account = accountResponse!.Data!;
        account.AccountRevision.Should().Be(2);
        account.OutstandingMinor.Should().Be(1000);
        account.AvailableMinor.Should().Be(1000);
        account.AvailableAllocations.Should().ContainSingle(value => value.OrderId == balance.OrderId
            && value.OrderItemId == balance.ItemId && value.AmountMinor == 1000);
        account.AvailableAllocations.Should().NotContain(value => value.OrderId == _orderId,
            "the refunded amendment unit is no longer payable");

        using var floorResponse = await server.GetAsync("/api/staff/server-workspace/floor");
        floorResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var floor = (await floorResponse.Content.ReadFromJsonAsync<
            ApiResponse<ServerFloorSnapshotDto>>(JsonOptions))!.Data!;
        var table = floor.Tables.Single(value => value.TableId == balance.TableId);
        var floorSession = table.Session!;
        floorSession.ServiceSessionId.Should().Be(_sessionId);
        floorSession.Remaining.Should().Be(10m);
        floorSession.CanCollect.Should().BeTrue();
        floorSession.CanRequestPaymentHandoff.Should().BeFalse();
        table.PermittedActions.Should().Contain("CollectPayment");

        var refundedUnitQuote = new CreateAccountPaymentQuoteRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 2,
            Mode = AccountPaymentMode.Items,
            PaymentMethod = PaymentMethod.CreditCard,
            SelectedUnits = [new AccountPaymentUnitSelection(_orderId, _itemId, 1)]
        };
        using var refusedQuote = await server.PostAsJsonAsync($"{accountPath}/quotes", refundedUnitQuote);
        refusedQuote.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var collectionQuote = new CreateAccountPaymentQuoteRequest
        {
            OperationId = Guid.NewGuid(),
            ExpectedAccountRevision = 2,
            Mode = AccountPaymentMode.Amount,
            PaymentMethod = PaymentMethod.CreditCard,
            AmountMinor = 1000
        };
        using var collectionQuoteResponse = await server.PostAsJsonAsync(
            $"{accountPath}/quotes", collectionQuote);
        collectionQuoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var operation = (await collectionQuoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<AccountPaymentOperationDto>>(JsonOptions))!.Data!;
        operation.AmountMinor.Should().Be(1000);
        operation.Allocations.Should().ContainSingle(value => value.OrderId == balance.OrderId
            && value.OrderItemId == balance.ItemId && value.AmountMinor == 1000);

        var operationPath = $"{accountPath}/operations/{operation.OperationId}";
        using var reserveResponse = await server.PostAsJsonAsync($"{operationPath}/reserve",
            new ReserveAccountPaymentRequest { ExpectedVersion = 1, ExpectedAccountRevision = 2 });
        reserveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reserved = (await reserveResponse.Content.ReadFromJsonAsync<
            ApiResponse<AccountPaymentOperationDto>>(JsonOptions))!.Data!;
        reserved.State.Should().Be(AccountPaymentState.Reserved);
        reserved.Version.Should().Be(2);

        using var captureResponse = await server.PostAsJsonAsync($"{operationPath}/collect",
            new CaptureAccountPaymentRequest { ExpectedVersion = 2 });
        captureResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var captured = (await captureResponse.Content.ReadFromJsonAsync<
            ApiResponse<AccountPaymentOperationDto>>(JsonOptions))!.Data!;
        captured.State.Should().Be(AccountPaymentState.Captured);
        captured.Version.Should().Be(3);

        var settledAccount = (await server.GetFromJsonAsync<ApiResponse<AccountPaymentAccountDto>>(
            accountPath, JsonOptions))!.Data!;
        settledAccount.OutstandingMinor.Should().Be(0);
        settledAccount.AvailableMinor.Should().Be(0);
        settledAccount.CapturedAccountPaymentMinor.Should().Be(3000);

        var sessionPath = $"/api/table-service-sessions/{_sessionId}";
        var session = (await server.GetFromJsonAsync<ApiResponse<TableServiceSessionDto>>(
            sessionPath, JsonOptions))!.Data!;
        session.Version.Should().Be(3);
        session.Outstanding.Should().Be(0m);
        session.CanCollect.Should().BeFalse();
        session.CanClose.Should().BeTrue();

        using var finalFloorResponse = await server.GetAsync("/api/staff/server-workspace/floor");
        var finalFloor = (await finalFloorResponse.Content.ReadFromJsonAsync<
            ApiResponse<ServerFloorSnapshotDto>>(JsonOptions))!.Data!;
        var settledTable = finalFloor.Tables.Single(value => value.TableId == balance.TableId);
        settledTable.Session!.CanClose.Should().BeTrue();
        settledTable.PermittedActions.Should().Contain("CloseVisit");
        settledTable.PermittedActions.Should().NotContain("CollectPayment");

        using var closeResponse = await server.PostAsJsonAsync(
            $"{sessionPath}/close", new CloseTableServiceSessionCommand { ExpectedVersion = session.Version });
        closeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = (await closeResponse.Content.ReadFromJsonAsync<
            ApiResponse<TableServiceSessionDto>>(JsonOptions))!.Data!;
        closed.Status.Should().Be(nameof(TableServiceSessionStatus.Closed));
        closed.Version.Should().Be(4);

        await using var verify = DatabaseFixture.CreateContext();
        (await verify.AccountPaymentAttempts.AnyAsync(value => value.OperationId == refundedUnitQuote.OperationId))
            .Should().BeFalse();
        (await verify.OrderBillingCredits.SingleAsync(value => value.AmendmentId == _amendmentId))
            .AmountMinor.Should().Be(1000);
        (await verify.AccountPaymentAllocationReversals.SingleAsync(value => value.OrderId == _orderId))
            .AmountMinor.Should().Be(1000);
        var refundedOrder = await verify.Orders.Include(value => value.Payments)
            .SingleAsync(value => value.Id == _orderId);
        refundedOrder.BillingCreditAmount.Should().Be(10m);
        refundedOrder.TotalPaid.Should().Be(10m);
        refundedOrder.RemainingAmount.Should().Be(0m);
        refundedOrder.Payments.Single(value => value.Id == _paymentId).RefundedAmount.Should().Be(10m);
        var collectedOrder = await verify.Orders.Include(value => value.Payments)
            .SingleAsync(value => value.Id == balance.OrderId);
        collectedOrder.TotalPaid.Should().Be(10m);
        collectedOrder.RemainingAmount.Should().Be(0m);
        collectedOrder.Payments.Should().ContainSingle(value => value.Amount == 10m
            && value.PaymentMethod == PaymentMethod.CreditCard
            && value.Status == PaymentStatus.Completed);
        (await verify.TableServiceSessions.SingleAsync(value => value.Id == _sessionId))
            .Status.Should().Be(TableServiceSessionStatus.Closed);
    }

    private async Task<ServerBridgeBalance> SeedServerBridgeBalanceAsync()
    {
        var tableId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var label = $"SB-{tableId:N}"[..10];
        var table = new Table
        {
            Id = tableId,
            TableNumber = label,
            MaxGuests = 4,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        };
        await using var context = DatabaseFixture.CreateContext();
        var session = await context.TableServiceSessions.SingleAsync(value => value.Id == _sessionId);
        session.TableId = tableId;
        session.TableNumber = null;
        session.Table = table;
        var refundedOrder = await context.Orders.SingleAsync(value => value.Id == _orderId);
        refundedOrder.TableId = tableId;
        refundedOrder.TableLabel = label;
        refundedOrder.Table = table;
        var item = new OrderItem
        {
            Id = itemId,
            OrderId = orderId,
            ProductName = "Unpaid bridge meal",
            Quantity = 1,
            UnitPrice = 10m,
            ItemTotal = 10m,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        };
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"SB-{orderId:N}"[..15],
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Pending,
            TableId = tableId,
            TableLabel = label,
            Table = table,
            ServiceSessionId = _sessionId,
            ServiceSession = session,
            SubTotal = 10m,
            Total = 10m,
            TotalPaid = 0m,
            RemainingAmount = 10m,
            OrderDate = DateTime.UtcNow,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests),
            Items = [item]
        };
        context.Tables.Add(table);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        refundedOrder.Version.Should().Be(2, "assigning the configured table advances the tracked source order version");
        session.AccountRevision.Should().Be(1);
        return new ServerBridgeBalance(tableId, orderId, itemId);
    }

    private sealed record ServerBridgeBalance(Guid TableId, Guid OrderId, Guid ItemId);
}
