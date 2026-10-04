using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.TenantFeatures;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 4")]
public sealed partial class AccountCashRefundIntegrationTests(DatabaseFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _attemptId = Guid.NewGuid();
    private CashRefundCase[] _cases = [];
    private DateOnly _reportDate;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<ITenantFeatures>();
        services.AddSingleton<ITenantFeatures>(new TenantFeatures(Options.Create(
            new TenantFeatureSettings { OrderAmendmentsV1 = true })));
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();
        var now = FixedNow.UtcDateTime;
        _reportDate = DateOnly.FromDateTime(now);
        var session = new TableServiceSession
        {
            Id = _sessionId,
            TableNumber = 41,
            Currency = "CHF",
            OpenedAt = now,
            CreatedBy = nameof(AccountCashRefundIntegrationTests)
        };
        var cases = new[] { (ExactMinor: 1L, Name: "one"), (ExactMinor: 2L, Name: "two"),
            (ExactMinor: 330L, Name: "remainder") }
            .Select(value => BuildCase(value.ExactMinor, value.Name, now, _attemptId)).ToArray();
        var allAllocations = cases.Select(value => value.Allocation).ToArray();
        var snapshot = new AccountPaymentQuoteSnapshot(
            1, AccountPaymentMode.Amount, PaymentMethod.Cash, 333, "CHF", now.AddHours(1),
            null, null, allAllocations.Select(value => new AccountPaymentAllocationDto(
                value.OrderId, value.OrderItemId, value.StartOrdinal, value.UnitCount,
                value.MinorPerUnit, value.AmountMinor)).ToArray(),
            AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, 333));
        var actor = new AccountPaymentActor(Guid.NewGuid(), AccountPaymentActorKind.Staff,
            "cashier:cash-refund-test", UserRole.Cashier);
        var attempt = new AccountPaymentAttempt
        {
            Id = _attemptId,
            ServiceSessionId = _sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = actor.ActorId,
            ActorKind = actor.Kind,
            Mode = AccountPaymentMode.Amount,
            State = AccountPaymentState.Reserved,
            PaymentMethod = PaymentMethod.Cash,
            Version = 1,
            ExpectedAccountRevision = 1,
            AmountMinor = 333,
            Currency = "CHF",
            PayloadHash = new string('a', 64),
            SnapshotJson = AccountPaymentSnapshots.Serialize(snapshot),
            QuoteExpiresAt = snapshot.QuoteExpiresAt,
            CreatedAt = now.AddMinutes(-1),
            CreatedBy = actor.AuditIdentifier,
            Allocations = allAllocations
        };
        var captureRequest = new CaptureAccountPaymentRequest
        {
            ExpectedVersion = attempt.Version,
            ReceivedMinor = 400
        };
        var receipt = AccountCashCaptureReceiptPolicy.Create(attempt, snapshot, actor,
            captureRequest, actor.AuditIdentifier, now)!;
        attempt.State = AccountPaymentState.Captured;
        attempt.Version++;
        attempt.CompletedAt = now;
        attempt.CashCollectionReceipt = receipt;

        await using var context = DatabaseFixture.CreateContext();
        context.TableServiceSessions.Add(session);
        context.Orders.AddRange(cases.Select(value => value.Order));
        context.OrderAmendments.AddRange(cases.Select(value => value.Amendment));
        context.AccountPaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        _cases = cases.Select(value => new CashRefundCase(value.Order.Id,
            value.Amendment.Id, value.Payment.Id, value.Item.Id, value.ExactMinor)).ToArray();
    }

    private SeededCashRefundCase BuildCase(long exactMinor, string name, DateTime now, Guid attemptId)
    {
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var amendmentId = Guid.NewGuid();
        var amount = exactMinor / 100m;
        var item = new OrderItem
        {
            Id = itemId,
            ProductName = $"Cash refund {name}",
            Quantity = 1,
            UnitPrice = amount,
            ItemTotal = amount,
            CreatedBy = nameof(AccountCashRefundIntegrationTests)
        };
        var payment = new OrderPayment
        {
            Id = paymentId,
            PaymentMethod = PaymentMethod.Cash,
            Amount = amount,
            Currency = "CHF",
            Status = PaymentStatus.Completed,
            PaymentDate = now,
            CreatedAt = now,
            CreatedBy = nameof(AccountCashRefundIntegrationTests)
        };
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"CR-{orderId:N}"[..15],
            ServiceSessionId = _sessionId,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            Version = 1,
            SubTotal = amount,
            Total = amount,
            TotalPaid = amount,
            RemainingAmount = 0m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(AccountCashRefundIntegrationTests),
            Items = [item],
            Payments = [payment]
        };
        var change = new OrderAmendmentChangeSnapshot(itemId, OrderAmendmentChangeKind.Void,
            1, 1, false, new RestaurantSystem.Api.Features.Orders.Dtos.OrderItemDto
            {
                Id = itemId,
                ProductName = item.ProductName,
                Quantity = 1,
                UnitPrice = amount,
                ItemTotal = amount
            }, null);
        var preview = new OrderAmendmentFinancialPreviewDto("CHF", 0, exactMinor,
            -exactMinor, exactMinor, OrderAmendmentFinancialResolutionStatus.Pending,
            OrderAmendmentCreditState.PendingAllocationReview,
            OrderAmendmentLoyaltyState.None, OrderAmendmentRefundState.PendingTillRefund);
        var amendment = new OrderAmendment
        {
            Id = amendmentId,
            SourceOrderId = orderId,
            ServiceSessionId = _sessionId,
            ActorUserId = Guid.Parse(RestaurantSystem.IntegrationTests.Common.TestAuthHandler.AdminUserId),
            ActorRole = UserRole.Admin.ToString(),
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('b', 64),
            CommitPayloadHash = new string('c', 64),
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            CommittedAccountRevision = 1,
            ExpiresAt = now.AddHours(1),
            CommittedAt = now,
            RequestJson = "{}",
            ChangesJson = OrderAmendmentJson.Serialize(new[] { change }),
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = OrderAmendmentJson.Serialize(preview),
            CreatedAt = now,
            CreatedBy = nameof(AccountCashRefundIntegrationTests)
        };
        var allocation = new AccountPaymentAllocation
        {
            Id = Guid.NewGuid(),
            AttemptId = attemptId,
            OrderId = orderId,
            OrderItemId = itemId,
            OrderPaymentId = paymentId,
            StartOrdinal = 1,
            UnitCount = 1,
            MinorPerUnit = exactMinor,
            AmountMinor = exactMinor,
            CreatedBy = nameof(AccountCashRefundIntegrationTests)
        };
        return new SeededCashRefundCase(order, amendment, payment, item, allocation, exactMinor);
    }

    private sealed record SeededCashRefundCase(Order Order, OrderAmendment Amendment,
        OrderPayment Payment, OrderItem Item, AccountPaymentAllocation Allocation, long ExactMinor);

    private sealed record CashRefundCase(Guid OrderId, Guid AmendmentId, Guid PaymentId,
        Guid ItemId, long ExactMinor);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
