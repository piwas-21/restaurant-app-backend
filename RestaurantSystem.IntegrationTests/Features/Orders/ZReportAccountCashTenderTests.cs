using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantSystem.Api.Features.Orders.Queries.GetZReportQuery;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Common;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

[Collection("Database Lane 2")]
public sealed class ZReportAccountCashTenderTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime DayOne = new(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
    private static int _nextTableNumber;

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Net_cash_uses_cash_due_after_change_by_capture_date_and_currency()
    {
        await using (var context = fixture.CreateContext())
        {
            AddAccountTender(context, "positive-rounding", DayOne.AddHours(10),
                currency: "CHF", exactMinor: 113, tipMinor: 5, dueMinor: 115,
                receivedMinor: 120);
            AddAccountTender(context, "negative-rounding", DayOne.AddDays(1).AddHours(10),
                currency: "CHF", exactMinor: 102, tipMinor: 0, dueMinor: 100,
                receivedMinor: 107);
            AddAccountTender(context, "foreign-currency", DayOne.AddHours(11),
                currency: "EUR", exactMinor: 250, tipMinor: 0, dueMinor: 250,
                receivedMinor: 260);
            await context.SaveChangesAsync();
        }

        var firstDay = await ReadNetCashAsync(DayOne);
        firstDay.Should().BeEquivalentTo(new Dictionary<string, long>
        {
            ["CHF"] = 115,
            ["EUR"] = 250
        });

        var secondDay = await ReadNetCashAsync(DayOne.AddDays(1));
        secondDay.Should().BeEquivalentTo(new Dictionary<string, long> { ["CHF"] = 100 });
    }

    [Fact]
    public async Task Net_cash_relocates_finalized_cash_refunds_to_physical_return_dates_once()
    {
        await using (var context = fixture.CreateContext())
        {
            AddAccountTender(context, "positive-refund", DayOne.AddHours(9),
                currency: "CHF", exactMinor: 113, tipMinor: 0, dueMinor: 115,
                receivedMinor: 115, refundDate: DayOne.AddDays(2).AddHours(12),
                refundExactMinor: 108, cashReturnedMinor: 110,
                observedAt: DayOne.AddDays(1).AddHours(12));
            AddAccountTender(context, "negative-refund", DayOne.AddHours(10),
                currency: "CHF", exactMinor: 102, tipMinor: 0, dueMinor: 100,
                receivedMinor: 100, refundDate: DayOne.AddDays(3).AddHours(12),
                refundExactMinor: 100, cashReturnedMinor: 95,
                observedAt: DayOne.AddDays(2).AddHours(12));
            await context.SaveChangesAsync();
        }

        var dayOne = await ReadNetCashAsync(DayOne);
        var dayTwo = await ReadNetCashAsync(DayOne.AddDays(1));
        dayOne.Should().BeEquivalentTo(
            new Dictionary<string, long> { ["CHF"] = 215 });
        dayTwo.Should().BeEquivalentTo(
            new Dictionary<string, long> { ["CHF"] = -110 });
        var dayThree = await ReadNetCashAsync(DayOne.AddDays(2));
        var dayFour = await ReadNetCashAsync(DayOne.AddDays(3));
        dayThree.Should().BeEquivalentTo(
            new Dictionary<string, long> { ["CHF"] = -95 });
        dayFour.Should().BeEquivalentTo(
            new Dictionary<string, long> { ["CHF"] = 0 },
            "the exact refund is removed on its database date after its physical cash return was already reported");
        new[] { dayOne, dayTwo, dayThree, dayFour }.SelectMany(value => value.Values).Sum()
            .Should().Be(10, "the original 215 CHF collection less physical returns of 110 and 95 leaves 10");
    }

    [Fact]
    public async Task Net_cash_caps_refund_relocation_to_the_amount_applied_to_the_payment_ledger()
    {
        await using (var context = fixture.CreateContext())
        {
            AddAccountTender(context, "cumulative-mismatch", DayOne.AddHours(9),
                currency: "CHF", exactMinor: 200, tipMinor: 0, dueMinor: 200,
                receivedMinor: 200, refundDate: DayOne.AddDays(1).AddHours(9),
                refundExactMinor: 200, cashReturnedMinor: 200,
                observedAt: DayOne.AddHours(11), paymentRefundedMinor: 100);
            await context.SaveChangesAsync();
        }

        var physicalReturnDay = await ReadNetCashAsync(DayOne);
        var refundLedgerDay = await ReadNetCashAsync(DayOne.AddDays(1));
        physicalReturnDay.Should().BeEquivalentTo(
            new Dictionary<string, long> { ["CHF"] = 0 },
            "the 200 CHF capture and its physical 200 CHF return occurred on the same day");
        refundLedgerDay.Should().BeEquivalentTo(
            new Dictionary<string, long> { ["CHF"] = 0 },
            "only the 100 CHF refund reflected in the payment ledger is restored from its refund date");
        (physicalReturnDay.Values.Sum() + refundLedgerDay.Values.Sum()).Should().Be(0,
            "the payment has no remaining physical cash after the confirmed 200 CHF return");
    }

    [Fact]
    public async Task Net_cash_keeps_exact_fallback_for_legacy_and_unfinalized_refunds()
    {
        await using (var context = fixture.CreateContext())
        {
            AddLegacyCashPayment(context, "legacy-no-evidence", DayOne.AddHours(8),
                amountMinor: 300, refundedMinor: 100, refundDate: DayOne.AddDays(1).AddHours(8));
            AddAccountTender(context, "not-finalized", DayOne.AddHours(10),
                currency: "CHF", exactMinor: 300, tipMinor: 0, dueMinor: 300,
                receivedMinor: 300,
                refundExactMinor: 100, cashReturnedMinor: 100,
                observedAt: DayOne.AddHours(12), finalized: false);
            await context.SaveChangesAsync();
        }

        var captureAndObservedReturnsDay = await ReadNetCashAsync(DayOne);
        var refundLedgerDay = await ReadNetCashAsync(DayOne.AddDays(1));
        captureAndObservedReturnsDay.Should().BeEquivalentTo(
            new Dictionary<string, long> { ["CHF"] = 500 },
            "the 600 CHF legacy and account cash captures are reduced by a 100 CHF observed physical return");
        refundLedgerDay.Should().BeEquivalentTo(
            new Dictionary<string, long> { ["CHF"] = -100 },
            "the legacy refund remains exact, while unfinished evidence creates no refund-date adjustment");
        (captureAndObservedReturnsDay.Values.Sum() + refundLedgerDay.Values.Sum())
            .Should().Be(400,
                "the 600 captured, less 100 physically returned and 100 legacy exact refund, leaves 400");
    }

    private static void AddAccountTender(
        ApplicationDbContext context,
        string name,
        DateTime capturedAt,
        string currency,
        long exactMinor,
        long tipMinor,
        long dueMinor,
        long receivedMinor,
        DateTime? refundDate = null,
        long? refundExactMinor = null,
        long? cashReturnedMinor = null,
        DateTime? observedAt = null,
        long? paymentRefundedMinor = null,
        bool finalized = true)
    {
        var sessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var paymentMinor = exactMinor - tipMinor;
        var appliedRefundMinor = paymentRefundedMinor ?? (finalized ? refundExactMinor : null);
        var refundStatus = appliedRefundMinor.GetValueOrDefault() <= 0
            ? PaymentStatus.Completed
            : appliedRefundMinor >= paymentMinor
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
        var order = BuildOrder(orderId, sessionId, capturedAt, paymentId,
            paymentMinor, currency, refundStatus, refundDate, appliedRefundMinor);
        var session = new TableServiceSession
        {
            Id = sessionId,
            TableNumber = Interlocked.Increment(ref _nextTableNumber),
            Currency = currency,
            OpenedAt = capturedAt.AddHours(-1),
            CreatedAt = capturedAt.AddHours(-1),
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };
        var attempt = new AccountPaymentAttempt
        {
            Id = attemptId,
            ServiceSessionId = sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = actorId,
            ActorKind = AccountPaymentActorKind.Staff,
            Mode = AccountPaymentMode.Amount,
            State = AccountPaymentState.Captured,
            PaymentMethod = PaymentMethod.Cash,
            Version = 2,
            ExpectedAccountRevision = 1,
            AmountMinor = paymentMinor,
            TipMinor = tipMinor,
            Currency = currency,
            PayloadHash = new string('a', 64),
            SnapshotJson = "{}",
            QuoteExpiresAt = capturedAt.AddHours(1),
            CompletedAt = capturedAt,
            CreatedAt = capturedAt,
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };
        var receipt = new AccountCashCollectionReceipt
        {
            Id = receiptId,
            AttemptId = attemptId,
            PolicyVersion = currency == "CHF" ? "chf-cash-5-rappen-v1" : "exact-v1",
            Currency = currency,
            PaymentMethod = PaymentMethod.Cash,
            ExactAmountMinor = exactMinor,
            AdjustmentMinor = dueMinor - exactMinor,
            DueAmountMinor = dueMinor,
            ReceivedMinor = receivedMinor,
            ChangeMinor = receivedMinor - dueMinor,
            ExpectedAccountRevision = 1,
            ExpectedVersion = 1,
            RequestHash = new string('b', 64),
            ActorId = actorId,
            ActorKind = AccountPaymentActorKind.Staff,
            ActorRole = UserRole.Cashier,
            CapturedAt = capturedAt,
            CreatedAt = capturedAt,
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };

        context.TableServiceSessions.Add(session);
        context.Orders.Add(order);
        context.AccountPaymentAttempts.Add(attempt);
        context.AccountCashCollectionReceipts.Add(receipt);
        if (refundExactMinor is null || cashReturnedMinor is null || observedAt is null)
            return;

        AddRefundEvidence(context, name, order, paymentId, attempt, receipt, actorId,
            refundDate ?? observedAt.Value, refundExactMinor.Value, cashReturnedMinor.Value,
            observedAt.Value, finalized);
    }

    private static void AddRefundEvidence(
        ApplicationDbContext context,
        string name,
        Order order,
        Guid paymentId,
        AccountPaymentAttempt attempt,
        AccountCashCollectionReceipt receipt,
        Guid actorId,
        DateTime refundDate,
        long refundExactMinor,
        long cashReturnedMinor,
        DateTime observedAt,
        bool finalized)
    {
        var amendmentId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var legId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var amendment = new OrderAmendment
        {
            Id = amendmentId,
            SourceOrderId = order.Id,
            ServiceSessionId = order.ServiceSessionId,
            ActorUserId = actorId,
            ActorRole = UserRole.Admin.ToString(),
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('c', 64),
            CommitPayloadHash = new string('d', 64),
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            CommittedAccountRevision = 1,
            ExpiresAt = refundDate.AddHours(1),
            CommittedAt = observedAt,
            RequestJson = "{}",
            ChangesJson = "[]",
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = "{}",
            CreatedAt = observedAt,
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };
        var operation = new OrderAmendmentResolutionOperation
        {
            Id = operationId,
            AmendmentId = amendmentId,
            SourceOrderId = order.Id,
            ServiceSessionId = order.ServiceSessionId,
            ClientOperationId = Guid.NewGuid(),
            ActorUserId = actorId,
            ActorRole = UserRole.Admin.ToString(),
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            Currency = receipt.Currency,
            CreditMinor = refundExactMinor,
            RefundMinor = refundExactMinor,
            UnpaidWaivedMinor = 0,
            RequestHash = new string('e', 64),
            SnapshotJson = "{}",
            State = finalized
                ? OrderAmendmentResolutionOperationState.Resolved
                : OrderAmendmentResolutionOperationState.ReconciliationRequired,
            StartedAt = observedAt,
            ResolvedAt = finalized ? refundDate : null,
            CreatedAt = observedAt,
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };
        var leg = new OrderAmendmentRefundLeg
        {
            Id = legId,
            OperationId = operationId,
            SourcePaymentId = paymentId,
            AccountPaymentAttemptId = attempt.Id,
            Custody = OrderAmendmentRefundCustody.ManualTill,
            State = OrderAmendmentRefundLegState.Succeeded,
            AmountMinor = refundExactMinor,
            Currency = receipt.Currency,
            FrozenScopesJson = "[]",
            ManualTillReference = $"till-{name}",
            ResolvedAt = observedAt,
            CreatedAt = observedAt,
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };
        var intent = new AccountCashRefundIntent
        {
            Id = intentId,
            RefundLegId = legId,
            OperationId = operationId,
            AttemptId = attempt.Id,
            CollectionReceiptId = receipt.Id,
            PolicyVersion = receipt.PolicyVersion,
            Currency = receipt.Currency,
            OriginalExactAmountMinor = receipt.ExactAmountMinor,
            OriginalAdjustmentMinor = receipt.AdjustmentMinor,
            OriginalDueAmountMinor = receipt.DueAmountMinor,
            PreviouslyRefundedExactMinor = 0,
            PreviouslyRefundedCashMinor = 0,
            ExactRefundAmountMinor = refundExactMinor,
            RefundAdjustmentMinor = cashReturnedMinor - refundExactMinor,
            CashRefundAmountMinor = cashReturnedMinor,
            RetainedExactAmountMinor = receipt.ExactAmountMinor - refundExactMinor,
            RetainedCashDueMinor = receipt.DueAmountMinor - cashReturnedMinor,
            PriorHistoryFingerprint = new string('f', 64),
            CreatedAt = observedAt,
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };
        var accountEvidence = new AccountCashRefundEvidence
        {
            Id = Guid.NewGuid(),
            IntentId = intentId,
            ExactRefundAmountMinor = refundExactMinor,
            RefundAdjustmentMinor = cashReturnedMinor - refundExactMinor,
            CashReturnedMinor = cashReturnedMinor,
            Currency = receipt.Currency,
            ActorId = actorId,
            ActorRole = UserRole.Admin,
            TillReference = leg.ManualTillReference,
            ObservedAt = observedAt,
            CreatedAt = observedAt,
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };
        var operationEvidence = new OrderAmendmentRefundEvidence
        {
            Id = Guid.NewGuid(),
            RefundLegId = legId,
            Sequence = 1,
            Kind = OrderAmendmentRefundEvidenceKind.ManualTillConfirmation,
            State = OrderAmendmentRefundLegState.Succeeded,
            AmountMinor = refundExactMinor,
            Currency = receipt.Currency,
            ActorUserId = actorId,
            ActorRole = UserRole.Admin.ToString(),
            ObservedAt = observedAt,
            TillReference = leg.ManualTillReference,
            EvidenceJson = "{}",
            CreatedAt = observedAt,
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };

        context.OrderAmendments.Add(amendment);
        context.OrderAmendmentResolutionOperations.Add(operation);
        context.OrderAmendmentRefundLegs.Add(leg);
        context.AccountCashRefundIntents.Add(intent);
        context.AccountCashRefundEvidence.Add(accountEvidence);
        context.OrderAmendmentRefundEvidence.Add(operationEvidence);
    }

    private static Order BuildOrder(
        Guid orderId,
        Guid sessionId,
        DateTime createdAt,
        Guid paymentId,
        long paymentMinor,
        string currency,
        PaymentStatus status,
        DateTime? refundDate,
        long? refundedMinor)
    {
        var amount = paymentMinor / 100m;
        var payment = new OrderPayment
        {
            Id = paymentId,
            PaymentMethod = PaymentMethod.Cash,
            Amount = amount,
            Currency = currency.ToLowerInvariant(),
            Status = status,
            PaymentDate = createdAt,
            IsRefunded = status == PaymentStatus.Refunded,
            RefundedAmount = refundedMinor is null ? null : refundedMinor.Value / 100m,
            RefundDate = refundDate,
            CreatedAt = createdAt,
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"ZR-{orderId:N}"[..15],
            ServiceSessionId = sessionId,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            PaymentStatus = status,
            SubTotal = amount,
            Total = amount,
            TotalPaid = amount,
            RemainingAmount = 0,
            OrderDate = createdAt,
            CreatedAt = createdAt,
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };
        order.Payments.Add(payment);
        return order;
    }

    private static void AddLegacyCashPayment(
        ApplicationDbContext context,
        string name,
        DateTime capturedAt,
        long amountMinor,
        long refundedMinor,
        DateTime refundDate)
    {
        var orderId = Guid.NewGuid();
        var amount = amountMinor / 100m;
        var payment = new OrderPayment
        {
            Id = Guid.NewGuid(),
            PaymentMethod = PaymentMethod.Cash,
            Amount = amount,
            Currency = "chf",
            Status = PaymentStatus.PartiallyRefunded,
            PaymentDate = capturedAt,
            RefundedAmount = refundedMinor / 100m,
            RefundDate = refundDate,
            CreatedAt = capturedAt,
            CreatedBy = nameof(ZReportAccountCashTenderTests)
        };
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"ZR-{orderId:N}"[..15],
            Type = OrderType.Takeaway,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.PartiallyRefunded,
            SubTotal = amount,
            Total = amount,
            TotalPaid = amount,
            RemainingAmount = 0,
            OrderDate = capturedAt,
            CreatedAt = capturedAt,
            CreatedBy = name
        };
        payment.OrderId = orderId;
        order.Payments.Add(payment);
        context.Orders.Add(order);
    }

    private async Task<IReadOnlyDictionary<string, long>> ReadNetCashAsync(DateTime day)
    {
        await using var context = fixture.CreateContext();
        var totals = await ZReportTenderTotalsReader.ReadAsync(
            context, day, day.AddDays(1), new OrderDisplayCurrencyResolver(context), CancellationToken.None);
        return totals.NetCashCollected.ToDictionary(value => value.Currency!, value => value.AmountMinor);
    }
}
