using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed partial class AccountPaymentStaffCollectionEndpointTests
{
    [Fact]
    public async Task Quote_admission_allows_the_last_attempt_and_rejects_the_next_without_persisting_it()
    {
        var account = await Seed();
        await using (var context = fixture.CreateContext())
        {
            var now = DateTime.UtcNow;
            var attempts = new List<AccountPaymentAttempt>();
            var allocations = new List<AccountPaymentAllocation>();
            var limit = RestaurantSystem.Api.Settings.AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit;
            var expiresAt = now.AddMinutes(1);
            var allocationDto = new AccountPaymentAllocationDto(account.OrderId, account.ItemId,
                1, 1, 1000, 1000);
            var snapshot = new AccountPaymentQuoteSnapshot(1, AccountPaymentMode.Items,
                PaymentMethod.CreditCard, 1000, "CHF", expiresAt, null, null, [allocationDto]);
            var snapshotJson = AccountPaymentSnapshots.Serialize(snapshot);
            for (var index = 0; index < limit - 1; index++)
            {
                var attemptId = Guid.NewGuid();
                attempts.Add(new AccountPaymentAttempt
                {
                    Id = attemptId,
                    ServiceSessionId = account.SessionId,
                    OperationId = Guid.NewGuid(),
                    ActorId = Guid.NewGuid(),
                    ActorKind = AccountPaymentActorKind.Staff,
                    Mode = AccountPaymentMode.Items,
                    State = AccountPaymentState.Released,
                    PaymentMethod = PaymentMethod.CreditCard,
                    Version = 2,
                    ExpectedAccountRevision = 1,
                    AmountMinor = 1000,
                    Currency = "CHF",
                    PayloadHash = new string('a', 64),
                    SnapshotJson = snapshotJson,
                    QuoteExpiresAt = expiresAt,
                    CreatedAt = now,
                    CreatedBy = nameof(Quote_admission_allows_the_last_attempt_and_rejects_the_next_without_persisting_it)
                });
                allocations.Add(new AccountPaymentAllocation
                {
                    Id = Guid.NewGuid(),
                    AttemptId = attemptId,
                    OrderId = account.OrderId,
                    OrderItemId = account.ItemId,
                    StartOrdinal = 1,
                    UnitCount = 1,
                    MinorPerUnit = 1000,
                    AmountMinor = 1000,
                    CreatedAt = now,
                    CreatedBy = nameof(Quote_admission_allows_the_last_attempt_and_rejects_the_next_without_persisting_it)
                });
            }

            context.AccountPaymentAttempts.AddRange(attempts);
            context.AccountPaymentAllocations.AddRange(allocations);
            await context.SaveChangesAsync();
        }

        using var factory = Factory(payments: true, optIn: false);
        using var cashier = Client(factory, "Cashier");
        var lastRequest = Quote(account);
        using var acceptedResponse = await cashier.PostAsJsonAsync(
            $"{Route(account.SessionId)}/quotes", lastRequest);
        acceptedResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "a quote that brings both bounded ledgers to exactly 10,000 remains supported");

        using var rejectedResponse = await cashier.PostAsJsonAsync(
            $"{Route(account.SessionId)}/quotes", Quote(account));
        rejectedResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var verify = fixture.CreateContext();
        (await verify.AccountPaymentAttempts.CountAsync(value => value.ServiceSessionId == account.SessionId))
            .Should().Be(RestaurantSystem.Api.Settings.AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit);
        (await verify.AccountPaymentAllocations.CountAsync()).Should().Be(
            RestaurantSystem.Api.Settings.AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit);
    }
}

public sealed partial class AccountCashRefundIntegrationTests
{
    [Fact]
    public async Task Pending_cash_refund_reserves_frozen_reversals_across_distinct_receipts_in_the_same_session()
    {
        AuthenticateAsAdmin();
        var limit = RestaurantSystem.Api.Settings.AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit;
        await SeedUnrelatedAllocationReversalBudgetAsync(limit - 1);

        var first = await QuoteCashRefundAsync(_cases[0]);
        var secondCase = await SeedAdditionalCashReceiptAsync(330);
        var second = await QuoteCashRefundAsync(secondCase);
        var firstRequest = new OrderAmendmentResolutionStartRequest
        {
            Quote = first.Request,
            QuoteHash = first.Quote.QuoteHash,
            ExpiresAt = first.Quote.ExpiresAt
        };
        using var firstResponse = await Client.PostAsJsonAsync(first.Path, firstRequest, JsonOptions);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstOutcome = (await firstResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        firstOutcome.Outcome.Should().Be("accepted");
        var firstResult = firstOutcome.Result!;
        first.Quote.RefundLegs.Should().ContainSingle().Which.Scopes.Should().ContainSingle();
        var firstLeg = firstResult.RefundLegs.Should().ContainSingle().Subject;
        firstLeg.State.Should().Be("Pending");
        firstLeg.CashRefund.Should().NotBeNull();

        var secondRequest = new OrderAmendmentResolutionStartRequest
        {
            Quote = second.Request,
            QuoteHash = second.Quote.QuoteHash,
            ExpiresAt = second.Quote.ExpiresAt
        };
        await using (var pendingCheck = DatabaseFixture.CreateContext())
        {
            var pendingCapacity = await AccountCashRefundHistoryCapacityReader.ReadResolutionSizeAsync(
                pendingCheck, _sessionId, CancellationToken.None);
            pendingCapacity.AllocationReversals.Should().Be(limit,
                "the pending first receipt must reserve its one frozen scope beside 9,999 persisted rows");
            pendingCapacity.CanAdd(AccountCashRefundHistoryCapacityGrowth.ForResolution([1]))
                .Should().BeFalse("the pending scope consumes the final available reversal row");
        }

        using var blockedSecondStart = await Client.PostAsJsonAsync(second.Path, secondRequest, JsonOptions);
        blockedSecondStart.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blockedSecondStart.Content.ReadAsStringAsync())
            .Should().Contain("Resolve the earlier paid correction in this table account first.");
        await using (var blockedCheck = DatabaseFixture.CreateContext())
        {
            (await blockedCheck.OrderAmendmentResolutionOperations.CountAsync(value =>
                value.AmendmentId == secondCase.AmendmentId)).Should().Be(0);
            (await blockedCheck.OrderAmendmentResolutionRefusals.CountAsync(value =>
                value.ClientOperationId == second.Request.ClientOperationId)).Should().Be(0);
        }

        using var confirmation = await Client.PostAsJsonAsync(
            $"/api/staff/amendment-financial-resolution-operations/{firstResult.OperationId}/confirm-till",
            new ManualTillConfirmationRequest
            {
                PaymentId = _cases[0].PaymentId,
                TillReference = "pending-capacity-reservation-return",
                CashReturnedMinor = firstLeg.CashRefund!.CashRefundAmountMinor
            }, JsonOptions);
        confirmation.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalized = (await confirmation.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionResultDto>>(JsonOptions))!.Data!;
        finalized.State.Should().Be("Resolved");

        await using (var verify = DatabaseFixture.CreateContext())
        {
            var capacity = await AccountCashRefundHistoryCapacityReader.ReadResolutionSizeAsync(
                verify, _sessionId, CancellationToken.None);
            capacity.AllocationReversals.Should().Be(limit,
                "finalization must atomically replace the pending scope reservation with one persisted reversal");

            var secondAttemptId = await verify.AccountPaymentAllocations.AsNoTracking()
                .Where(value => value.OrderId == secondCase.OrderId)
                .Select(value => value.AttemptId).SingleAsync();
            var receiptHistory = await AccountCashRefundHistoryReader.ReadAsync(
                verify, [_attemptId, secondAttemptId], CancellationToken.None);
            receiptHistory[_attemptId].RefundedExactMinor.Should().Be(_cases[0].ExactMinor);
            receiptHistory[secondAttemptId].RefundedExactMinor.Should().Be(0);
            (await verify.AccountPaymentAllocationReversals.CountAsync()).Should().Be(limit);
        }

        using var firstReplayResponse = await Client.PostAsJsonAsync(first.Path, firstRequest, JsonOptions);
        firstReplayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstReplay = (await firstReplayResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        firstReplay.Outcome.Should().Be("accepted");
        firstReplay.Result!.OperationId.Should().Be(firstResult.OperationId);

        var refreshedSecond = await QuoteCashRefundAsync(secondCase, second.Request.ClientOperationId);
        var refreshedSecondRequest = new OrderAmendmentResolutionStartRequest
        {
            Quote = refreshedSecond.Request,
            QuoteHash = refreshedSecond.Quote.QuoteHash,
            ExpiresAt = refreshedSecond.Quote.ExpiresAt
        };
        using var refusedResponse = await Client.PostAsJsonAsync(
            refreshedSecond.Path, refreshedSecondRequest, JsonOptions);
        refusedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refused = (await refusedResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        refused.Outcome.Should().Be("refused");
        refused.Refusal!.FailureCode.Should().Be("cashHistoryCapacityExceeded");

        using var refusedReplayResponse = await Client.PostAsJsonAsync(
            refreshedSecond.Path, refreshedSecondRequest, JsonOptions);
        refusedReplayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refusedReplay = (await refusedReplayResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        refusedReplay.Outcome.Should().Be("refused");
        refusedReplay.Refusal.Should().BeEquivalentTo(refused.Refusal);
        await using var finalCheck = DatabaseFixture.CreateContext();
        (await finalCheck.OrderAmendmentResolutionOperations.CountAsync(value =>
            value.AmendmentId == secondCase.AmendmentId)).Should().Be(0);
        (await finalCheck.OrderAmendmentResolutionRefusals.CountAsync(value =>
            value.ClientOperationId == second.Request.ClientOperationId)).Should().Be(1);
    }

    [Fact]
    public async Task Resolution_capacity_accepts_last_projected_receipt_row_then_persists_and_replays_overflow_refusal()
    {
        AuthenticateAsAdmin();
        await AddUnrelatedReceiptHistoryAsync(
            RestaurantSystem.Api.Settings.AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit - 1);

        var first = await QuoteCashRefundAsync(_cases[0]);
        var firstStartRequest = new OrderAmendmentResolutionStartRequest
        {
            Quote = first.Request,
            QuoteHash = first.Quote.QuoteHash,
            ExpiresAt = first.Quote.ExpiresAt
        };
        using var acceptedResponse = await Client.PostAsJsonAsync(first.Path, firstStartRequest, JsonOptions);
        acceptedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var accepted = (await acceptedResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        accepted.Outcome.Should().Be("accepted");
        var acceptedLeg = accepted.Result!.RefundLegs.Should().ContainSingle().Subject;
        acceptedLeg.CashRefund.Should().NotBeNull();

        using var confirmation = await Client.PostAsJsonAsync(
            $"/api/staff/amendment-financial-resolution-operations/{accepted.Result.OperationId}/confirm-till",
            new ManualTillConfirmationRequest
            {
                PaymentId = _cases[0].PaymentId,
                TillReference = "capacity-boundary-return",
                CashReturnedMinor = acceptedLeg.CashRefund!.CashRefundAmountMinor
            }, JsonOptions);
        confirmation.StatusCode.Should().Be(HttpStatusCode.OK,
            "the operation admitted at the boundary must still confirm, finalize, and project its frozen result");
        var finalized = (await confirmation.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionResultDto>>(JsonOptions))!.Data!;
        finalized.State.Should().Be("Resolved");
        finalized.RefundLegs.Should().ContainSingle().Which.CashReturn!.CashReturnedMinor
            .Should().Be(acceptedLeg.CashRefund.CashRefundAmountMinor);

        await using (var read = DatabaseFixture.CreateContext())
        {
            var history = await AccountCashRefundHistoryReader.ReadAsync(
                read, [_attemptId], CancellationToken.None);
            history[_attemptId].RefundedExactMinor.Should().Be(_cases[0].ExactMinor);
            (await read.OrderAmendmentRefundLegs.CountAsync()).Should().Be(
                RestaurantSystem.Api.Settings.AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit);
        }

        using var acceptedReplayResponse = await Client.PostAsJsonAsync(first.Path, firstStartRequest, JsonOptions);
        acceptedReplayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var acceptedReplay = (await acceptedReplayResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        acceptedReplay.Outcome.Should().Be("accepted");
        acceptedReplay.Result!.OperationId.Should().Be(accepted.Result.OperationId,
            "the original client operation key must replay before new-history capacity admission");

        var second = await QuoteCashRefundAsync(_cases[1]);
        var request = new OrderAmendmentResolutionStartRequest
        {
            Quote = second.Request,
            QuoteHash = second.Quote.QuoteHash,
            ExpiresAt = second.Quote.ExpiresAt
        };
        using var refusedResponse = await Client.PostAsJsonAsync(second.Path, request, JsonOptions);
        refusedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var refused = (await refusedResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        refused.Outcome.Should().Be("refused");
        refused.Refusal!.FailureCode.Should().Be("cashHistoryCapacityExceeded");

        using var replayResponse = await Client.PostAsJsonAsync(second.Path, request, JsonOptions);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        replay.Outcome.Should().Be("refused");
        replay.Refusal.Should().BeEquivalentTo(refused.Refusal);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderAmendmentResolutionOperations.CountAsync(value => value.AmendmentId == _cases[1].AmendmentId))
            .Should().Be(0);
        (await verify.OrderAmendmentRefundLegs.CountAsync()).Should().Be(
            RestaurantSystem.Api.Settings.AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit);
        (await verify.AccountCashRefundIntents.CountAsync()).Should().Be(1);
        (await verify.OrderAmendmentResolutionRefusals.CountAsync(value =>
            value.ClientOperationId == second.Request.ClientOperationId)).Should().Be(1);
    }

    [Fact]
    public async Task Accepted_client_key_replays_after_unrelated_cash_history_grows_to_the_limit()
    {
        AuthenticateAsAdmin();
        var quoted = await QuoteCashRefundAsync(_cases[0]);
        var request = new OrderAmendmentResolutionStartRequest
        {
            Quote = quoted.Request,
            QuoteHash = quoted.Quote.QuoteHash,
            ExpiresAt = quoted.Quote.ExpiresAt
        };

        using var initialResponse = await Client.PostAsJsonAsync(quoted.Path, request, JsonOptions);
        initialResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var initial = (await initialResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        initial.Outcome.Should().Be("accepted");

        await AddUnrelatedReceiptHistoryAsync(
            RestaurantSystem.Api.Settings.AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit - 1);

        using var replayResponse = await Client.PostAsJsonAsync(quoted.Path, request, JsonOptions);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        replay.Outcome.Should().Be("accepted");
        replay.Result!.OperationId.Should().Be(initial.Result!.OperationId);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderAmendmentRefundLegs.CountAsync()).Should().Be(
            RestaurantSystem.Api.Settings.AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit);
        (await verify.OrderAmendmentResolutionOperations.CountAsync(value =>
            value.AmendmentId == _cases[0].AmendmentId)).Should().Be(1);
    }

    [Fact]
    public async Task Existing_overflow_is_recorded_before_source_history_reconciliation_can_fail()
    {
        AuthenticateAsAdmin();
        var quoted = await QuoteCashRefundAsync(_cases[0]);
        var request = new OrderAmendmentResolutionStartRequest
        {
            Quote = quoted.Request,
            QuoteHash = quoted.Quote.QuoteHash,
            ExpiresAt = quoted.Quote.ExpiresAt
        };
        await AddUnrelatedReceiptHistoryAsync(
            RestaurantSystem.Api.Settings.AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit + 1,
            _attemptId);

        using var response = await Client.PostAsJsonAsync(quoted.Path, request, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var refusal = (await response.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        refusal.Outcome.Should().Be("refused");
        refusal.Refusal!.FailureCode.Should().Be("cashHistoryCapacityExceeded");

        using var replayResponse = await Client.PostAsJsonAsync(quoted.Path, request, JsonOptions);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionStartOutcomeDto>>(JsonOptions))!.Data!;
        replay.Outcome.Should().Be("refused");
        replay.Refusal.Should().BeEquivalentTo(refusal.Refusal);
        await using var verify = DatabaseFixture.CreateContext();
        (await verify.OrderAmendmentResolutionOperations.CountAsync(value =>
            value.AmendmentId == _cases[0].AmendmentId)).Should().Be(0);
        (await verify.OrderAmendmentRefundLegs.CountAsync()).Should().Be(
            RestaurantSystem.Api.Settings.AccountPaymentSettings.AbsoluteCashRefundHistoryRowLimit + 1);
        (await verify.OrderAmendmentResolutionRefusals.CountAsync(value =>
            value.ClientOperationId == quoted.Request.ClientOperationId)).Should().Be(1);
    }

    private async Task<CashCapacityQuote> QuoteCashRefundAsync(
        CashRefundCase item, Guid? clientOperationId = null)
    {
        var path = $"/api/staff/orders/{item.OrderId}/amendments/{item.AmendmentId}/financial-resolution";
        using var contextResponse = await Client.GetAsync($"{path}/context");
        contextResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var context = (await contextResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionContextDto>>(JsonOptions))!.Data!;
        var request = new OrderAmendmentResolutionQuoteRequest
        {
            ClientOperationId = clientOperationId ?? Guid.NewGuid(),
            ExpectedOrderVersion = context.ExpectedOrderVersion,
            ExpectedAccountRevision = context.ExpectedAccountRevision,
            Currency = context.Currency,
            ManualRefunds = []
        };
        using var quoteResponse = await Client.PostAsJsonAsync($"{path}/quote", request, JsonOptions);
        quoteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var quote = (await quoteResponse.Content.ReadFromJsonAsync<
            ApiResponse<OrderAmendmentResolutionQuoteDto>>(JsonOptions))!.Data!;
        return new(path, request, quote);
    }

    private async Task AddUnrelatedReceiptHistoryAsync(int legCount, Guid? linkedAttemptId = null)
    {
        var now = FixedNow.UtcDateTime;
        var actor = new AccountPaymentActor(Guid.NewGuid(), AccountPaymentActorKind.Staff,
            "cashier:capacity-fixture", UserRole.Cashier);
        var settlement = AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, 333);
        var snapshot = new AccountPaymentQuoteSnapshot(1, AccountPaymentMode.Amount, PaymentMethod.Cash,
            333, "CHF", FixedNow.AddHours(1).UtcDateTime, null, null, [], settlement);
        var dummyAttempt = new AccountPaymentAttempt
        {
            Id = Guid.NewGuid(),
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
            CreatedAt = now,
            CreatedBy = actor.AuditIdentifier
        };
        var receipt = AccountCashCaptureReceiptPolicy.Create(dummyAttempt, snapshot, actor,
            new CaptureAccountPaymentRequest { ExpectedVersion = 1, ReceivedMinor = 335 },
            actor.AuditIdentifier, now)!;
        dummyAttempt.State = AccountPaymentState.Captured;
        dummyAttempt.Version++;
        dummyAttempt.CompletedAt = now;
        dummyAttempt.CashCollectionReceipt = receipt;

        var unrelatedOrder = new Order
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"CAP-{Guid.NewGuid():N}"[..15],
            ServiceSessionId = _sessionId,
            Type = OrderType.DineIn,
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Completed,
            Version = 1,
            SubTotal = legCount / 100m,
            Total = legCount / 100m,
            TotalPaid = legCount / 100m,
            RemainingAmount = 0m,
            OrderDate = now,
            CreatedAt = now,
            CreatedBy = nameof(AddUnrelatedReceiptHistoryAsync)
        };
        var payments = Enumerable.Range(0, legCount).Select(_ => new OrderPayment
        {
            Id = Guid.NewGuid(),
            OrderId = unrelatedOrder.Id,
            PaymentMethod = PaymentMethod.Cash,
            Amount = 0.01m,
            Currency = "CHF",
            Status = PaymentStatus.Completed,
            PaymentDate = now,
            CreatedAt = now,
            CreatedBy = nameof(AddUnrelatedReceiptHistoryAsync)
        }).ToArray();
        unrelatedOrder.Payments = payments;

        var amendmentId = Guid.NewGuid();
        var clientOperationId = Guid.NewGuid();
        var amendment = new OrderAmendment
        {
            Id = amendmentId,
            SourceOrderId = unrelatedOrder.Id,
            ServiceSessionId = _sessionId,
            ClientOperationId = clientOperationId,
            ActorUserId = Guid.Parse(TestAuthHandler.AdminUserId),
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
            ChangesJson = "[]",
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = "{}",
            CreatedAt = now,
            CreatedBy = nameof(AddUnrelatedReceiptHistoryAsync)
        };
        var operation = new OrderAmendmentResolutionOperation
        {
            Id = Guid.NewGuid(),
            AmendmentId = amendmentId,
            SourceOrderId = unrelatedOrder.Id,
            ServiceSessionId = _sessionId,
            ClientOperationId = Guid.NewGuid(),
            ActorUserId = Guid.Parse(TestAuthHandler.AdminUserId),
            ActorRole = UserRole.Admin.ToString(),
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            Currency = "CHF",
            CreditMinor = legCount,
            RefundMinor = legCount,
            UnpaidWaivedMinor = 0,
            RequestHash = new string('d', 64),
            SnapshotJson = "{}",
            State = OrderAmendmentResolutionOperationState.Resolved,
            StartedAt = now,
            ResolvedAt = now,
            CreatedAt = now,
            CreatedBy = nameof(AddUnrelatedReceiptHistoryAsync)
        };
        operation.Legs = payments.Select(payment => new OrderAmendmentRefundLeg
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            SourcePaymentId = payment.Id,
            AccountPaymentAttemptId = linkedAttemptId ?? dummyAttempt.Id,
            Custody = OrderAmendmentRefundCustody.ManualTill,
            State = OrderAmendmentRefundLegState.Succeeded,
            AmountMinor = 1,
            Currency = "CHF",
            FrozenScopesJson = "[]",
            CreatedAt = now,
            CreatedBy = nameof(AddUnrelatedReceiptHistoryAsync)
        }).ToArray();

        await using var context = DatabaseFixture.CreateContext();
        context.AccountPaymentAttempts.Add(dummyAttempt);
        context.Orders.Add(unrelatedOrder);
        context.OrderAmendments.Add(amendment);
        context.OrderAmendmentResolutionOperations.Add(operation);
        await context.SaveChangesAsync();
    }

    // Synthetic capacity budget only; this is not a complete unrelated refund-history witness.
    private async Task SeedUnrelatedAllocationReversalBudgetAsync(int reversalCount)
    {
        var now = FixedNow.UtcDateTime;
        var actor = new AccountPaymentActor(Guid.NewGuid(), AccountPaymentActorKind.Staff,
            "cashier:capacity-reversal-fixture", UserRole.Cashier);
        const long minorPerUnit = 5;
        var exactMinor = checked((long)reversalCount * minorPerUnit);
        var settlement = AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, exactMinor);
        var allocationId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var amendmentId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var legId = Guid.NewGuid();
        var amount = exactMinor / 100m;
        var expiresAt = FixedNow.AddHours(1).UtcDateTime;
        var allocationDto = new AccountPaymentAllocationDto(
            orderId, itemId, 1, reversalCount, minorPerUnit, exactMinor);
        var snapshot = new AccountPaymentQuoteSnapshot(1, AccountPaymentMode.Amount,
            PaymentMethod.Cash, exactMinor, "CHF", expiresAt, null, null,
            [allocationDto], settlement);
        var item = new OrderItem
        {
            Id = itemId,
            ProductName = "Unrelated reversed cash units",
            Quantity = reversalCount,
            UnitPrice = minorPerUnit / 100m,
            ItemTotal = amount,
            CreatedAt = now,
            CreatedBy = nameof(SeedUnrelatedAllocationReversalBudgetAsync)
        };
        var payment = new OrderPayment
        {
            Id = paymentId,
            PaymentMethod = PaymentMethod.Cash,
            Amount = amount,
            Currency = "CHF",
            Status = PaymentStatus.Refunded,
            PaymentDate = now,
            IsRefunded = true,
            RefundedAmount = amount,
            RefundDate = now,
            RefundReason = "Unrelated capacity fixture",
            CreatedAt = now,
            CreatedBy = nameof(SeedUnrelatedAllocationReversalBudgetAsync)
        };
        var order = new Order
        {
            Id = orderId,
            OrderNumber = $"CAP-{orderId:N}"[..15],
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
            CreatedBy = nameof(SeedUnrelatedAllocationReversalBudgetAsync),
            Items = [item],
            Payments = [payment]
        };
        var allocation = new AccountPaymentAllocation
        {
            Id = allocationId,
            AttemptId = attemptId,
            OrderId = orderId,
            OrderItemId = itemId,
            OrderPaymentId = paymentId,
            StartOrdinal = 1,
            UnitCount = reversalCount,
            MinorPerUnit = minorPerUnit,
            AmountMinor = exactMinor,
            CreatedAt = now,
            CreatedBy = nameof(SeedUnrelatedAllocationReversalBudgetAsync)
        };
        var attempt = new AccountPaymentAttempt
        {
            Id = attemptId,
            ServiceSessionId = _sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = actor.ActorId,
            ActorKind = actor.Kind,
            Mode = AccountPaymentMode.Amount,
            State = AccountPaymentState.Reserved,
            PaymentMethod = PaymentMethod.Cash,
            Version = 1,
            ExpectedAccountRevision = 1,
            AmountMinor = exactMinor,
            Currency = "CHF",
            PayloadHash = new string('a', 64),
            SnapshotJson = AccountPaymentSnapshots.Serialize(snapshot),
            QuoteExpiresAt = expiresAt,
            CreatedAt = now.AddMinutes(-1),
            CreatedBy = actor.AuditIdentifier,
            Allocations = [allocation]
        };
        var receipt = AccountCashCaptureReceiptPolicy.Create(attempt, snapshot, actor,
            new CaptureAccountPaymentRequest
            {
                ExpectedVersion = attempt.Version,
                ReceivedMinor = settlement.DueAmountMinor
            }, actor.AuditIdentifier, now)!;
        attempt.State = AccountPaymentState.Captured;
        attempt.Version++;
        attempt.CompletedAt = now;
        attempt.CashCollectionReceipt = receipt;

        var amendment = new OrderAmendment
        {
            Id = amendmentId,
            SourceOrderId = orderId,
            ServiceSessionId = _sessionId,
            ClientOperationId = Guid.NewGuid(),
            ActorUserId = Guid.Parse(TestAuthHandler.AdminUserId),
            ActorRole = UserRole.Admin.ToString(),
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('b', 64),
            CommitPayloadHash = new string('c', 64),
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            CommittedAccountRevision = 1,
            ExpiresAt = expiresAt,
            CommittedAt = now,
            RequestJson = "{}",
            ChangesJson = "[]",
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = "{}",
            CreatedAt = now,
            CreatedBy = nameof(SeedUnrelatedAllocationReversalBudgetAsync)
        };
        var operation = new OrderAmendmentResolutionOperation
        {
            Id = operationId,
            AmendmentId = amendmentId,
            SourceOrderId = orderId,
            ServiceSessionId = _sessionId,
            ClientOperationId = Guid.NewGuid(),
            ActorUserId = Guid.Parse(TestAuthHandler.AdminUserId),
            ActorRole = UserRole.Admin.ToString(),
            ExpectedOrderVersion = 1,
            ExpectedAccountRevision = 1,
            Currency = "CHF",
            CreditMinor = exactMinor,
            RefundMinor = exactMinor,
            UnpaidWaivedMinor = 0,
            RequestHash = new string('d', 64),
            SnapshotJson = "{}",
            State = OrderAmendmentResolutionOperationState.Resolved,
            StartedAt = now,
            ResolvedAt = now,
            CreatedAt = now,
            CreatedBy = nameof(SeedUnrelatedAllocationReversalBudgetAsync)
        };
        var frozenScopes = System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new
            {
                allocationId,
                orderId,
                orderItemId = (Guid?)itemId,
                startOrdinal = 1,
                unitCount = reversalCount,
                minorPerUnit,
                amountMinor = exactMinor
            }
        });
        var leg = new OrderAmendmentRefundLeg
        {
            Id = legId,
            OperationId = operationId,
            SourcePaymentId = paymentId,
            AccountPaymentAttemptId = attemptId,
            Custody = OrderAmendmentRefundCustody.ManualTill,
            State = OrderAmendmentRefundLegState.Succeeded,
            AmountMinor = exactMinor,
            Currency = "CHF",
            FrozenScopesJson = frozenScopes,
            ManualTillReference = "unrelated-capacity-fixture",
            ResolvedAt = now,
            CreatedAt = now,
            CreatedBy = nameof(SeedUnrelatedAllocationReversalBudgetAsync)
        };
        operation.Legs = [leg];
        var reversals = Enumerable.Range(1, reversalCount).Select(startOrdinal =>
            new AccountPaymentAllocationReversal
            {
                Id = Guid.NewGuid(),
                AllocationId = allocationId,
                RefundLegId = legId,
                OrderId = orderId,
                OrderItemId = itemId,
                StartOrdinal = startOrdinal,
                UnitCount = 1,
                MinorPerUnit = minorPerUnit,
                AmountMinor = minorPerUnit,
                Currency = "CHF",
                ActorUserId = Guid.Parse(TestAuthHandler.AdminUserId),
                ActorRole = UserRole.Admin.ToString(),
                ReversedAt = now,
                CreatedAt = now,
                CreatedBy = nameof(SeedUnrelatedAllocationReversalBudgetAsync)
            }).ToArray();

        await using var context = DatabaseFixture.CreateContext();
        context.Orders.Add(order);
        context.OrderAmendments.Add(amendment);
        context.AccountPaymentAttempts.Add(attempt);
        context.OrderAmendmentResolutionOperations.Add(operation);
        context.AccountPaymentAllocationReversals.AddRange(reversals);
        await context.SaveChangesAsync();
    }

    private async Task<CashRefundCase> SeedAdditionalCashReceiptAsync(long exactMinor)
    {
        var now = FixedNow.UtcDateTime;
        var attemptId = Guid.NewGuid();
        var seeded = BuildCase(exactMinor, "capacity-second-receipt", now, attemptId);
        var actor = new AccountPaymentActor(Guid.NewGuid(), AccountPaymentActorKind.Staff,
            "cashier:capacity-second-receipt", UserRole.Cashier);
        var settlement = AccountCashSettlementPolicy.Resolve("CHF", PaymentMethod.Cash, exactMinor);
        var allocationDto = new AccountPaymentAllocationDto(seeded.Order.Id, seeded.Item.Id,
            seeded.Allocation.StartOrdinal, seeded.Allocation.UnitCount,
            seeded.Allocation.MinorPerUnit, seeded.Allocation.AmountMinor);
        var expiresAt = now.AddHours(1);
        var snapshot = new AccountPaymentQuoteSnapshot(1, AccountPaymentMode.Amount,
            PaymentMethod.Cash, exactMinor, "CHF", expiresAt, null, null,
            [allocationDto], settlement);
        var attempt = new AccountPaymentAttempt
        {
            Id = attemptId,
            ServiceSessionId = _sessionId,
            OperationId = Guid.NewGuid(),
            ActorId = actor.ActorId,
            ActorKind = actor.Kind,
            Mode = AccountPaymentMode.Amount,
            State = AccountPaymentState.Reserved,
            PaymentMethod = PaymentMethod.Cash,
            Version = 1,
            ExpectedAccountRevision = 1,
            AmountMinor = exactMinor,
            Currency = "CHF",
            PayloadHash = new string('e', 64),
            SnapshotJson = AccountPaymentSnapshots.Serialize(snapshot),
            QuoteExpiresAt = expiresAt,
            CreatedAt = now,
            CreatedBy = actor.AuditIdentifier,
            Allocations = [seeded.Allocation]
        };
        var receipt = AccountCashCaptureReceiptPolicy.Create(attempt, snapshot, actor,
            new CaptureAccountPaymentRequest
            {
                ExpectedVersion = attempt.Version,
                ReceivedMinor = settlement.DueAmountMinor
            }, actor.AuditIdentifier, now)!;
        attempt.State = AccountPaymentState.Captured;
        attempt.Version++;
        attempt.CompletedAt = now;
        attempt.CashCollectionReceipt = receipt;

        await using var context = DatabaseFixture.CreateContext();
        context.Orders.Add(seeded.Order);
        context.OrderAmendments.Add(seeded.Amendment);
        context.AccountPaymentAttempts.Add(attempt);
        await context.SaveChangesAsync();
        return new CashRefundCase(seeded.Order.Id, seeded.Amendment.Id,
            seeded.Payment.Id, seeded.Item.Id, exactMinor);
    }

    private sealed record CashCapacityQuote(
        string Path,
        OrderAmendmentResolutionQuoteRequest Request,
        OrderAmendmentResolutionQuoteDto Quote);
}
