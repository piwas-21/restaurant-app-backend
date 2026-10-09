using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Features.Orders.Commands.CancelOrderCommand;
using RestaurantSystem.Api.Features.Orders.Commands.UpdateOrderStatusCommand;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class OrderAmendmentStripeResolutionIntegrationTests
{
    [Theory]
    [InlineData("cancel")]
    [InlineData("status")]
    public async Task Fully_reversed_mixed_tender_can_be_cancelled_without_changing_refund_history(string entrance)
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: true);
        var beforeCancellation = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);
        beforeCancellation.Credits.Should().ContainSingle(value => value.AmountMinor == 2000);
        beforeCancellation.Reversals.Should().HaveCount(2);
        beforeCancellation.Reversals.Sum(value => value.AmountMinor).Should().Be(2000);
        beforeCancellation.Payments.Should().HaveCount(2)
            .And.OnlyContain(value => value.Status == PaymentStatus.Refunded
                && value.IsRefunded && value.RefundedAmount == value.Amount);

        using var cancellationResponse = await CancelAsync(entrance, beforeCancellation.OrderVersion);
        cancellationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancellationResponse.Content.ReadFromJsonAsync<ApiResponse<RestaurantSystem.Api.Features.Orders.Dtos.OrderDto>>(
            JsonOptions))!.Success.Should().BeTrue();

        var afterCancellation = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);
        afterCancellation.OrderStatus.Should().Be(OrderStatus.Cancelled);
        afterCancellation.Should().BeEquivalentTo(beforeCancellation,
            options => options.Excluding(value => value.OrderStatus).Excluding(value => value.OrderVersion));
        _refundState.CreateCalls.Should().Be(1,
            "cancelling a fully refunded order must not repeat a provider refund");
        _refundState.CreatedEvidence.Should().ContainSingle();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("status")]
    public async Task Partially_reversed_mixed_tender_still_blocks_cancellation(string entrance)
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: false);
        var beforeCancellation = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);
        beforeCancellation.Reversals.Should().HaveCount(2);
        beforeCancellation.Reversals.Sum(value => value.AmountMinor).Should().Be(1000);
        beforeCancellation.Payments.Should().OnlyContain(value => value.RefundedAmount.HasValue
            && value.RefundedAmount.Value > 0 && value.RefundedAmount.Value < value.Amount);

        using var cancellationResponse = await CancelAsync(entrance, beforeCancellation.OrderVersion);
        cancellationResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await ReadCancellationFinancialSnapshotAsync(settled.OperationId))
            .Should().BeEquivalentTo(beforeCancellation);
        _refundState.CreateCalls.Should().Be(1);
        _refundState.CreatedEvidence.Should().ContainSingle();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatch")]
    public async Task Captured_allocation_linked_tender_requires_exact_currency(string currencyCase)
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: true);
        await using (var corrupt = DatabaseFixture.CreateContext())
        {
            var payment = await corrupt.OrderPayments.SingleAsync(value => value.Id == settled.PaymentId);
            payment.Currency = currencyCase == "missing" ? null : "EUR";
            await corrupt.SaveChangesAsync();
        }

        await using var before = DatabaseFixture.CreateContext();
        var orderVersion = await before.Orders.Where(value => value.Id == _orderId)
            .Select(value => value.Version).SingleAsync();
        using var cancellationResponse = await CancelAsync("cancel", orderVersion);
        cancellationResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await using var read = DatabaseFixture.CreateContext();
        (await read.Orders.SingleAsync(value => value.Id == _orderId)).Status.Should().Be(OrderStatus.Pending);
        _refundState.CreateCalls.Should().Be(1);
        _refundState.CreatedEvidence.Should().ContainSingle();
    }

    [Fact]
    public async Task Checkout_reconciliation_flag_still_blocks_fully_reversed_cancellation()
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: true);
        await using (var corrupt = DatabaseFixture.CreateContext())
        {
            var journal = await corrupt.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == _attemptId);
            journal.ReconciliationRequired = true;
            await corrupt.SaveChangesAsync();
        }

        var before = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);
        using var cancellationResponse = await CancelAsync("cancel", before.OrderVersion);
        cancellationResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadCancellationFinancialSnapshotAsync(settled.OperationId)).Should().BeEquivalentTo(before);
        _refundState.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Stale_lower_checkout_refund_cache_does_not_override_resolved_refund_ledger()
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: true);
        await using (var stale = DatabaseFixture.CreateContext())
        {
            var journal = await stale.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == _attemptId);
            journal.ProviderRefundedMinor = 0;
            await stale.SaveChangesAsync();
        }

        var before = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);
        using var cancellationResponse = await CancelAsync("cancel", before.OrderVersion);
        cancellationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);
        after.Should().BeEquivalentTo(before,
            options => options.Excluding(value => value.OrderStatus).Excluding(value => value.OrderVersion));
        _refundState.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Current_cash_settlement_snapshot_without_its_capture_receipt_blocks_cancellation()
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: true);
        await using (var corrupt = DatabaseFixture.CreateContext())
        {
            var attempt = await corrupt.AccountPaymentAttempts
                .Include(value => value.CashCollectionReceipt)
                .SingleAsync(value => value.PaymentMethod == PaymentMethod.Cash
                    && value.Allocations.Any(allocation => allocation.OrderId == _orderId));
            attempt.CashCollectionReceipt.Should().BeNull();
            var snapshot = AccountPaymentSnapshots.Deserialize<AccountPaymentQuoteSnapshot>(attempt.SnapshotJson);
            attempt.SnapshotJson = AccountPaymentSnapshots.Serialize(snapshot with
            {
                CashSettlement = AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash,
                    attempt.AmountMinor)
            });
            await corrupt.SaveChangesAsync();
        }

        var before = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);
        using var cancellationResponse = await CancelAsync("cancel", before.OrderVersion);
        cancellationResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadCancellationFinancialSnapshotAsync(settled.OperationId)).Should().BeEquivalentTo(before);
        _refundState.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Fully_reversed_order_can_cancel_when_shared_attempt_still_captures_another_order()
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: true, shareCashAttemptWithOtherOrder: true);
        var before = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);

        using var cancellationResponse = await CancelAsync("cancel", before.OrderVersion);
        cancellationResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);
        after.Should().BeEquivalentTo(before,
            options => options.Excluding(value => value.OrderStatus).Excluding(value => value.OrderVersion));
        await using var read = DatabaseFixture.CreateContext();
        var other = await read.Orders.Include(value => value.Payments)
            .SingleAsync(value => value.Id == settled.OtherOrderId);
        other.Status.Should().Be(OrderStatus.Pending);
        other.Payments.Should().ContainSingle(value => value.Status == PaymentStatus.Completed
            && !value.RefundedAmount.HasValue && !value.IsRefunded);
        var sharedAttempt = await read.AccountPaymentAttempts.Include(value => value.Allocations)
            .SingleAsync(value => value.Id == settled.SharedAttemptId);
        sharedAttempt.State.Should().Be(AccountPaymentState.Captured);
        sharedAttempt.Allocations.Sum(value => value.AmountMinor).Should().Be(sharedAttempt.AmountMinor);
        (await read.AccountPaymentAllocationReversals.Where(value => value.OrderId == settled.OtherOrderId)
            .AnyAsync()).Should().BeFalse();
        _refundState.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Shared_attempt_with_foreign_allocation_bound_to_target_tender_requires_reconciliation()
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: true, shareCashAttemptWithOtherOrder: true);
        await using (var corrupt = DatabaseFixture.CreateContext())
        {
            var foreignAllocation = await corrupt.AccountPaymentAllocations.SingleAsync(value =>
                value.OrderId == settled.OtherOrderId);
            foreignAllocation.OrderPaymentId = settled.PaymentId;
            await corrupt.SaveChangesAsync();
        }

        await using var before = DatabaseFixture.CreateContext();
        var orderVersion = await before.Orders.Where(value => value.Id == _orderId)
            .Select(value => value.Version).SingleAsync();
        using var cancellationResponse = await CancelAsync("cancel", orderVersion);
        cancellationResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await before.Orders.SingleAsync(value => value.Id == _orderId)).Status.Should().Be(OrderStatus.Pending);
        _refundState.CreateCalls.Should().Be(1);
        _refundState.CreatedEvidence.Should().ContainSingle();
    }

    [Fact]
    public async Task Captured_attempt_tender_method_must_match_its_allocation_linked_payment()
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: true);
        await using (var corrupt = DatabaseFixture.CreateContext())
        {
            var payment = await corrupt.OrderPayments.SingleAsync(value => value.Id == _paymentId);
            payment.PaymentMethod = PaymentMethod.Cash;
            payment.PaymentGateway = null;
            payment.TransactionId = null;
            await corrupt.SaveChangesAsync();
        }

        await using var read = DatabaseFixture.CreateContext();
        var orderVersion = await read.Orders.Where(value => value.Id == _orderId)
            .Select(value => value.Version).SingleAsync();
        using var cancellationResponse = await CancelAsync("cancel", orderVersion);
        cancellationResponse.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a Stripe-captured attempt cannot be reclassified as a till tender by changing its linked payment");
        (await read.Orders.SingleAsync(value => value.Id == _orderId)).Status.Should().Be(OrderStatus.Pending);
        _refundState.CreateCalls.Should().Be(1);
        _refundState.CreatedEvidence.Should().ContainSingle();
    }

    [Fact]
    public async Task Shared_attempt_foreign_allocation_must_belong_to_the_attempt_visit()
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: true, shareCashAttemptWithOtherOrder: true);
        await using (var corrupt = DatabaseFixture.CreateContext())
        {
            var foreignOrder = await corrupt.Orders.SingleAsync(value => value.Id == settled.OtherOrderId);
            var foreignVisitId = Guid.NewGuid();
            corrupt.TableServiceSessions.Add(new TableServiceSession
            {
                Id = foreignVisitId,
                Currency = "CHF",
                OpenedAt = DateTime.UtcNow,
                CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
            });
            foreignOrder.ServiceSessionId = foreignVisitId;
            await corrupt.SaveChangesAsync();
        }

        await using var read = DatabaseFixture.CreateContext();
        var orderVersion = await read.Orders.Where(value => value.Id == _orderId)
            .Select(value => value.Version).SingleAsync();
        using var cancellationResponse = await CancelAsync("cancel", orderVersion);
        cancellationResponse.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a valid order/payment pair from another visit cannot be included in this visit's captured attempt");
        (await read.Orders.SingleAsync(value => value.Id == _orderId)).Status.Should().Be(OrderStatus.Pending);
        _refundState.CreateCalls.Should().Be(1);
        _refundState.CreatedEvidence.Should().ContainSingle();
    }

    [Theory]
    [InlineData("overreversal", "cancel")]
    [InlineData("overreversal", "status")]
    [InlineData("misbound", "cancel")]
    [InlineData("misbound", "status")]
    public async Task Inconsistent_reversal_history_still_blocks_cancellation(string corruption, string entrance)
    {
        var settled = await SettleMixedTenderAsync(fullyVoided: corruption == "overreversal");
        await AddInvalidReversalAsync(settled.OperationId, corruption);
        var beforeCancellation = await ReadCancellationFinancialSnapshotAsync(settled.OperationId);

        using var cancellationResponse = await CancelAsync(entrance, beforeCancellation.OrderVersion);
        cancellationResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await ReadCancellationFinancialSnapshotAsync(settled.OperationId))
            .Should().BeEquivalentTo(beforeCancellation);
        _refundState.CreateCalls.Should().Be(1);
        _refundState.CreatedEvidence.Should().ContainSingle();
    }

    private async Task<SettledMixedTender> SettleMixedTenderAsync(
        bool fullyVoided, bool shareCashAttemptWithOtherOrder = false)
    {
        var mixedTender = await PrepareMixedTenderForCancellationAsync(fullyVoided, shareCashAttemptWithOtherOrder);
        AuthenticateAsAdmin();
        var basePath = $"/api/staff/orders/{_orderId}/amendments/{_amendmentId}/financial-resolution";
        var quoteRequest = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = _clientOperationId,
            ExpectedOrderVersion = mixedTender.OrderVersion,
            ExpectedAccountRevision = mixedTender.AccountRevision,
            Currency = "CHF"
        };

        using var quoteResponse = await Client.PostAsJsonAsync($"{basePath}/quote", quoteRequest, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        var creditMinor = fullyVoided ? 2000 : 1000;
        var stripeRefundMinor = fullyVoided ? 1200 : 600;
        var tillRefundMinor = fullyVoided ? 800 : 400;
        quote.CreditMinor.Should().Be(creditMinor);
        quote.RefundMinor.Should().Be(creditMinor);
        quote.RefundLegs.Should().ContainSingle(value => value.PaymentId == _paymentId
            && value.Custody == "StripeDirect" && value.AmountMinor == stripeRefundMinor);
        quote.RefundLegs.Should().ContainSingle(value => value.PaymentId == mixedTender.PaymentId
            && value.Custody == "ManualTill" && value.AmountMinor == tillRefundMinor
            && value.RequiresTillConfirmation);

        var startRequest = new OrderAmendmentResolutionStartRequest
        {
            Quote = quoteRequest,
            QuoteHash = quote.QuoteHash,
            ExpiresAt = quote.ExpiresAt
        };
        using var startResponse = await Client.PostAsJsonAsync(basePath, startRequest, JsonOptions);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = (await startResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        started.Outcome.Should().Be("accepted");
        started.Result!.State.Should().Be("ReconciliationRequired");
        _refundState.CreateCalls.Should().Be(1);

        using var replayResponse = await Client.PostAsJsonAsync(basePath, startRequest, JsonOptions);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        replay.Outcome.Should().Be("accepted");
        replay.Result!.OperationId.Should().Be(started.Result.OperationId);
        replay.Result.State.Should().Be("Processing");
        replay.Result.RefundLegs.Should().ContainSingle(value => value.PaymentId == _paymentId
            && value.State == "Succeeded");
        replay.Result.RefundLegs.Should().ContainSingle(value => value.PaymentId == mixedTender.PaymentId
            && value.State == "Pending");

        await RecordCanonicalCheckoutRefundAsync(stripeRefundMinor);
        using var tillResponse = await Client.PostAsJsonAsync(
            $"/api/staff/amendment-financial-resolution-operations/{started.Result.OperationId}/confirm-till",
            new ManualTillConfirmationRequest
            {
                PaymentId = mixedTender.PaymentId,
                TillReference = "fully-void-mixed-refund"
            }, JsonOptions);
        tillResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var settled = (await tillResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionResultDto>>(JsonOptions))!.Data!;
        settled.State.Should().Be("Resolved");
        settled.RefundLegs.Should().HaveCount(2).And.OnlyContain(value => value.State == "Succeeded");
        _refundState.CreateCalls.Should().Be(1);
        _refundState.CreatedEvidence.Should().ContainSingle();
        return new SettledMixedTender(started.Result.OperationId, mixedTender.PaymentId,
            mixedTender.OtherOrderId, mixedTender.SharedAttemptId);
    }

    private async Task<HttpResponseMessage> CancelAsync(string entrance, int orderVersion) => entrance switch
    {
        "cancel" => await Client.PostAsJsonAsync($"/api/orders/{_orderId}/cancel", new CancelOrderCommand
        {
            OrderId = _orderId,
            ExpectedVersion = orderVersion,
            CancellationReason = "Fully refunded void"
        }, JsonOptions),
        "status" => await Client.PutAsJsonAsync($"/api/orders/{_orderId}/status", new UpdateOrderStatusCommand
        {
            OrderId = _orderId,
            ExpectedVersion = orderVersion,
            NewStatus = OrderStatus.Cancelled
        }, JsonOptions),
        _ => throw new ArgumentOutOfRangeException(nameof(entrance))
    };

    private async Task AddInvalidReversalAsync(Guid operationId, string corruption)
    {
        await using var context = DatabaseFixture.CreateContext();
        var operation = await context.OrderAmendmentResolutionOperations.Include(value => value.Legs)
            .SingleAsync(value => value.Id == operationId);
        var stripeLeg = operation.Legs.Single(value => value.Custody == OrderAmendmentRefundCustody.StripeDirect);
        var tillLeg = operation.Legs.Single(value => value.Custody == OrderAmendmentRefundCustody.ManualTill);
        var stripeReversal = await context.AccountPaymentAllocationReversals
            .SingleAsync(value => value.RefundLegId == stripeLeg.Id);
        var overreversal = corruption == "overreversal";
        context.AccountPaymentAllocationReversals.Add(new AccountPaymentAllocationReversal
        {
            Id = Guid.NewGuid(),
            AllocationId = stripeReversal.AllocationId,
            RefundLegId = overreversal ? stripeLeg.Id : tillLeg.Id,
            OrderId = _orderId,
            OrderItemId = stripeReversal.OrderItemId,
            StartOrdinal = stripeReversal.StartOrdinal + stripeReversal.UnitCount,
            UnitCount = 1,
            MinorPerUnit = stripeReversal.MinorPerUnit,
            AmountMinor = stripeReversal.MinorPerUnit,
            Currency = stripeReversal.Currency,
            ActorUserId = operation.ActorUserId,
            ActorRole = operation.ActorRole,
            ReversedAt = DateTime.UtcNow,
            CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
        });
        await context.SaveChangesAsync();
    }

    private async Task<(Guid PaymentId, int OrderVersion, long AccountRevision,
        Guid? OtherOrderId, Guid? SharedAttemptId)> PrepareMixedTenderForCancellationAsync(
            bool fullyVoided, bool shareCashAttemptWithOtherOrder)
    {
        var mixedTender = await PrepareMixedTenderAsync(sameUnit: false);
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.Include(value => value.ServiceSession)
            .SingleAsync(value => value.Id == _orderId);
        Guid? otherOrderId = null;
        Guid? sharedAttemptId = null;
        order.Status = OrderStatus.Pending;
        if (shareCashAttemptWithOtherOrder)
        {
            var now = DateTime.UtcNow;
            var foreignOrderId = Guid.NewGuid();
            var foreignItemId = Guid.NewGuid();
            var foreignPaymentId = Guid.NewGuid();
            var foreignOrder = new Order
            {
                Id = foreignOrderId,
                OrderNumber = $"SHARED-{foreignOrderId:N}"[..16],
                ServiceSessionId = _sessionId,
                Type = OrderType.DineIn,
                Status = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Completed,
                Version = 1,
                SubTotal = 2m,
                Total = 2m,
                TotalPaid = 2m,
                RemainingAmount = 0m,
                OrderDate = now,
                CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests),
                Items =
                [
                    new OrderItem
                    {
                        Id = foreignItemId,
                        ProductName = "Shared attempt meal",
                        Quantity = 1,
                        UnitPrice = 2m,
                        ItemTotal = 2m,
                        CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
                    }
                ],
                Payments =
                [
                    new OrderPayment
                    {
                        Id = foreignPaymentId,
                        PaymentMethod = PaymentMethod.Cash,
                        Amount = 2m,
                        Currency = "CHF",
                        Status = PaymentStatus.Completed,
                        PaymentDate = now,
                        CreatedAt = now,
                        CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
                    }
                ]
            };
            context.Orders.Add(foreignOrder);
            var sharedAttempt = await context.AccountPaymentAttempts
                .Include(value => value.Allocations)
                .SingleAsync(value => value.ServiceSessionId == _sessionId
                    && value.PaymentMethod == PaymentMethod.Cash);
            var foreignAllocation = new AccountPaymentAllocation
            {
                Id = Guid.NewGuid(),
                AttemptId = sharedAttempt.Id,
                OrderId = foreignOrderId,
                OrderItemId = foreignItemId,
                OrderPaymentId = foreignPaymentId,
                StartOrdinal = 1,
                UnitCount = 1,
                MinorPerUnit = 200,
                AmountMinor = 200,
                CreatedBy = nameof(OrderAmendmentStripeResolutionIntegrationTests)
            };
            sharedAttempt.Allocations.Add(foreignAllocation);
            context.AccountPaymentAllocations.Add(foreignAllocation);
            sharedAttempt.AmountMinor += 200;
            var savedSnapshot = AccountPaymentSnapshots.Deserialize<AccountPaymentQuoteSnapshot>(
                sharedAttempt.SnapshotJson);
            sharedAttempt.SnapshotJson = AccountPaymentSnapshots.Serialize(savedSnapshot with
            {
                AmountMinor = sharedAttempt.AmountMinor,
                Allocations = savedSnapshot.Allocations.Append(new AccountPaymentAllocationDto(
                    foreignOrderId, foreignItemId, 1, 1, 200, 200)).ToArray()
            });
            otherOrderId = foreignOrderId;
            sharedAttemptId = sharedAttempt.Id;
        }
        if (fullyVoided)
        {
            var amendment = await context.OrderAmendments.SingleAsync(value => value.Id == _amendmentId);
            var changes = OrderAmendmentJson.Deserialize<List<OrderAmendmentChangeSnapshot>>(amendment.ChangesJson);
            changes.Should().HaveCount(2);
            amendment.ChangesJson = OrderAmendmentJson.Serialize(changes.Select(value => value with
            {
                Quantity = 2,
                Previous = value.Previous with { Quantity = 2 }
            }).ToArray());
            var financial = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(
                amendment.FinancialResolutionJson);
            amendment.FinancialResolutionJson = OrderAmendmentJson.Serialize(financial with
            {
                RemovedUnitValueMinor = 2000,
                NetAccountDeltaMinor = -2000,
                PotentialCreditMinor = 2000
            });
        }
        await context.SaveChangesAsync();
        return (mixedTender.PaymentId, order.Version, order.ServiceSession!.AccountRevision,
            otherOrderId, sharedAttemptId);
    }

    private async Task<CancellationFinancialSnapshot> ReadCancellationFinancialSnapshotAsync(Guid operationId)
    {
        await using var context = DatabaseFixture.CreateContext();
        var order = await context.Orders.Include(value => value.Payments)
            .SingleAsync(value => value.Id == _orderId);
        var operation = await context.OrderAmendmentResolutionOperations
            .SingleAsync(value => value.Id == operationId);
        return new CancellationFinancialSnapshot(
            order.Version,
            order.Status,
            order.TotalPaid,
            order.BillingCreditAmount,
            order.RemainingAmount,
            await context.OrderPayments.Where(value => value.OrderId == _orderId)
                .OrderBy(value => value.Id).Select(value => new CancellationPaymentSnapshot(
                    value.Id, value.Amount, value.Status, value.IsRefunded, value.RefundedAmount,
                    value.RefundDate, value.RefundReason, value.UpdatedAt, value.UpdatedBy))
                .ToArrayAsync(),
            await context.OrderBillingCredits.Where(value => value.AmendmentId == _amendmentId)
                .OrderBy(value => value.Id).Select(value => new CancellationCreditSnapshot(
                    value.Id, value.AmountMinor, value.Currency, value.ActorUserId, value.ActorRole))
                .ToArrayAsync(),
            await context.AccountPaymentAllocationReversals.Where(value => value.OrderId == _orderId)
                .OrderBy(value => value.Id).Select(value => new CancellationReversalSnapshot(
                    value.Id, value.AllocationId, value.RefundLegId, value.OrderId, value.OrderItemId,
                    value.StartOrdinal, value.UnitCount, value.MinorPerUnit, value.AmountMinor,
                    value.Currency, value.ActorUserId, value.ActorRole))
                .ToArrayAsync(),
            operation.State,
            operation.ResultJson);
    }

    private sealed record SettledMixedTender(Guid OperationId, Guid PaymentId,
        Guid? OtherOrderId = null, Guid? SharedAttemptId = null);

    private sealed record CancellationFinancialSnapshot(
        int OrderVersion,
        OrderStatus OrderStatus,
        decimal TotalPaid,
        decimal BillingCreditAmount,
        decimal RemainingAmount,
        IReadOnlyList<CancellationPaymentSnapshot> Payments,
        IReadOnlyList<CancellationCreditSnapshot> Credits,
        IReadOnlyList<CancellationReversalSnapshot> Reversals,
        OrderAmendmentResolutionOperationState OperationState,
        string? OperationResultJson);

    private sealed record CancellationPaymentSnapshot(
        Guid Id, decimal Amount, PaymentStatus Status, bool IsRefunded, decimal? RefundedAmount,
        DateTime? RefundDate, string? RefundReason, DateTime? UpdatedAt, string? UpdatedBy);

    private sealed record CancellationCreditSnapshot(
        Guid Id, long AmountMinor, string Currency, Guid ActorUserId, string ActorRole);

    private sealed record CancellationReversalSnapshot(
        Guid Id, Guid AllocationId, Guid RefundLegId, Guid OrderId, Guid? OrderItemId,
        int StartOrdinal, int UnitCount, long MinorPerUnit, long AmountMinor, string Currency,
        Guid ActorUserId, string ActorRole);
}
