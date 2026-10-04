using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.IntegrationTests.Common;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed partial class AccountCheckoutJournalTests
{
    [Fact]
    public async Task Pending_refund_request_preserves_capture_but_cannot_prove_provider_money_returned()
    {
        var journal = await VerifyCapture();
        var ids = await AddResolutionEvidenceAsync(journal, includeManualSibling: false);
        await using var context = DatabaseFixture.CreateContext();

        var verified = await AccountCheckoutAmendmentRefundProof.ValidateAsync(
            context, journal, CaptureEvidence(journal), CancellationToken.None);
        verified.Should().Be(0, "the frozen request does not claim that Stripe returned money");

        var matchingProviderRefund = ProviderRefund(ids, "re_outcome_unknown");
        var appearsWithoutSavedObservation = () => AccountCheckoutAmendmentRefundProof.ValidateAsync(
            context, journal, CaptureEvidence(journal, 100, [matchingProviderRefund]), CancellationToken.None);
        await appearsWithoutSavedObservation.Should().ThrowAsync<ConflictException>();

        var sameAmountWrongIdentity = ProviderRefund(ids with { AttemptId = Guid.NewGuid() }, "re_outcome_unknown");
        var wrongIdentity = () => AccountCheckoutAmendmentRefundProof.ValidateAsync(
            context, journal, CaptureEvidence(journal, 100, [sameAmountWrongIdentity]), CancellationToken.None);
        await wrongIdentity.Should().ThrowAsync<ConflictException>();

        await using var readback = DatabaseFixture.CreateContext();
        var attempt = await readback.AccountPaymentAttempts.SingleAsync(value => value.Id == journal.AttemptId);
        attempt.State.Should().Be(AccountPaymentState.Captured,
            "an incomplete amendment refund must not erase or repost the original capture");
        (await readback.OrderPayments.CountAsync()).Should().Be(1);
        (await readback.OrderAmendmentRefundEvidence.CountAsync(value =>
            value.Kind == OrderAmendmentRefundEvidenceKind.ProviderObservation)).Should().Be(0);
    }

    [Fact]
    public async Task P8_reconciliation_records_the_bound_provider_leg_without_finalizing_a_pending_sibling()
    {
        var journal = await VerifyCapture();
        var ids = await AddResolutionEvidenceAsync(journal, includeManualSibling: true);
        await using (var context = DatabaseFixture.CreateContext())
        {
            var leg = await context.OrderAmendmentRefundLegs.SingleAsync(value => value.Id == ids.LegId);
            var attempt = await context.OrderAmendmentRefundAttempts.SingleAsync(value => value.Id == ids.AttemptId);
            var operation = await context.OrderAmendmentResolutionOperations
                .SingleAsync(value => value.Id == ids.OperationId);
            var manualLeg = await context.OrderAmendmentRefundLegs
                .SingleAsync(value => value.OperationId == ids.OperationId
                    && value.Custody == OrderAmendmentRefundCustody.ManualTill);
            var observation = NewRefundObservation(leg, attempt, "re_bound_refund", 2);
            context.OrderAmendmentRefundEvidence.Add(observation);
            leg.State = OrderAmendmentRefundLegState.Succeeded;
            leg.ResolvedAt = Now;
            operation.State = OrderAmendmentResolutionOperationState.Processing;
            await context.SaveChangesAsync();

            var partialEvidence = CaptureEvidence(journal, 100,
                [ProviderRefund(ids, "re_bound_refund")]);
            (await AccountCheckoutAmendmentRefundProof.ValidateAsync(
                context, journal, partialEvidence, CancellationToken.None))
                .Should().Be(100, "this evidence is scoped to the linked captured Stripe contribution");

            await using var writerContext = DatabaseFixture.CreateContext();
            journal = await new AccountCheckoutEvidenceWriter(writerContext,
                    Options.Create(new AccountCheckoutSettings()), new FixedClock())
                .RecordAsync(journal, partialEvidence, CancellationToken.None);
            journal.ProviderRefundedMinor.Should().Be(100);

            await using var partialReadback = DatabaseFixture.CreateContext();
            var stillPendingOperation = await partialReadback.OrderAmendmentResolutionOperations
                .Include(value => value.Legs)
                .SingleAsync(value => value.Id == ids.OperationId);
            stillPendingOperation.State.Should().Be(OrderAmendmentResolutionOperationState.Processing,
                "recording a canonical provider refund does not finalize the whole amendment");
            stillPendingOperation.Legs.Single(value => value.Id != ids.LegId).State
                .Should().Be(OrderAmendmentRefundLegState.Pending);
            (await partialReadback.OrderBillingCredits.AnyAsync(value =>
                value.AmendmentId == stillPendingOperation.AmendmentId)).Should().BeFalse();
            (await partialReadback.AccountPaymentAllocationReversals.AnyAsync(value =>
                value.RefundLegId == ids.LegId)).Should().BeFalse();
            var order = await partialReadback.Orders.Include(value => value.Payments)
                .SingleAsync(value => value.Id == stillPendingOperation.SourceOrderId);
            order.TotalPaid.Should().Be(3.34m);
            order.Payments.Should().OnlyContain(value => value.RefundedAmount == null,
                "the amendment ledger has not posted until all custody legs have proof");

            operation.State = OrderAmendmentResolutionOperationState.Resolved;
            manualLeg.State = OrderAmendmentRefundLegState.Succeeded;
            manualLeg.ResolvedAt = Now;
            manualLeg.ManualTillReference = "till-reference-123";
            context.OrderAmendmentRefundEvidence.Add(new OrderAmendmentRefundEvidence
            {
                Id = Guid.NewGuid(),
                RefundLegId = manualLeg.Id,
                Sequence = 1,
                Kind = OrderAmendmentRefundEvidenceKind.ManualTillConfirmation,
                State = OrderAmendmentRefundLegState.Succeeded,
                AmountMinor = manualLeg.AmountMinor,
                Currency = manualLeg.Currency,
                ActorUserId = Guid.Parse(TestAuthHandler.AdminUserId),
                ActorRole = "Admin",
                ObservedAt = Now,
                TillReference = manualLeg.ManualTillReference,
                EvidenceJson = "{}",
                CreatedBy = nameof(AccountCheckoutJournalTests)
            });
            await context.SaveChangesAsync();
        }

        await using var resolvedContext = DatabaseFixture.CreateContext();
        var canonical = await AccountCheckoutAmendmentRefundProof.ValidateAsync(
            resolvedContext, journal, CaptureEvidence(journal, 100,
                [ProviderRefund(ids, "re_bound_refund")]), CancellationToken.None);
        canonical.Should().Be(100);

        var substitutedRefundId = () => AccountCheckoutAmendmentRefundProof.ValidateAsync(
            resolvedContext, journal, CaptureEvidence(journal, 100,
                [ProviderRefund(ids, "re_same_amount_different_id")]), CancellationToken.None);
        await substitutedRefundId.Should().ThrowAsync<ConflictException>();

        var succeededRegression = () => AccountCheckoutAmendmentRefundProof.ValidateAsync(
            resolvedContext, journal, CaptureEvidence(journal, 0,
                [ProviderRefund(ids, "re_bound_refund", "failed")]), CancellationToken.None);
        await succeededRegression.Should().ThrowAsync<ConflictException>(
            "a terminal successful provider refund cannot regress to failed");

        var unexpectedSecondRefund = () => AccountCheckoutAmendmentRefundProof.ValidateAsync(
            resolvedContext, journal, CaptureEvidence(journal, 200,
                [ProviderRefund(ids, "re_bound_refund"), ProviderRefund(ids, "re_extra_refund")]),
            CancellationToken.None);
        await unexpectedSecondRefund.Should().ThrowAsync<ConflictException>(
            "one idempotent attempt cannot authorize a second refund identity");
    }

    private async Task<RefundFixtureIds> AddResolutionEvidenceAsync(
        AccountCheckoutJournal journal, bool includeManualSibling)
    {
        await using var context = DatabaseFixture.CreateContext();
        await Poster(context).PostAsync(journal, CancellationToken.None);
        var attempt = await context.AccountPaymentAttempts.SingleAsync(value => value.Id == journal.AttemptId);
        var payment = await context.OrderPayments.SingleAsync();
        var order = await context.Orders.SingleAsync(value => value.Id == payment.OrderId);
        var now = Now;
        var manualPaymentId = includeManualSibling ? Guid.NewGuid() : (Guid?)null;
        if (manualPaymentId.HasValue)
            context.OrderPayments.Add(new OrderPayment
            {
                Id = manualPaymentId.Value,
                OrderId = order.Id,
                PaymentMethod = PaymentMethod.Cash,
                Amount = 1m,
                Status = PaymentStatus.Completed,
                PaymentDate = now,
                CreatedBy = nameof(AccountCheckoutJournalTests)
            });
        var amendmentId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var legId = Guid.NewGuid();
        var refundAttemptId = Guid.NewGuid();
        var actorId = Guid.Parse(TestAuthHandler.AdminUserId);
        context.Set<OrderAmendment>().Add(new OrderAmendment
        {
            Id = amendmentId,
            SourceOrderId = order.Id,
            ServiceSessionId = attempt.ServiceSessionId,
            ActorUserId = actorId,
            ActorRole = "Admin",
            State = OrderAmendmentState.Committed,
            PayloadHash = new string('a', 64),
            CommitPayloadHash = new string('b', 64),
            ExpectedOrderVersion = order.Version,
            ExpectedAccountRevision = 1,
            CommittedAccountRevision = 2,
            ExpiresAt = now.AddMinutes(1),
            CommittedAt = now,
            RequestJson = "{}",
            ChangesJson = "[]",
            SourceSnapshotJson = "{}",
            FinancialResolutionJson = "{}",
            CreatedBy = nameof(AccountCheckoutJournalTests)
        });
        var operation = new OrderAmendmentResolutionOperation
        {
            Id = operationId,
            AmendmentId = amendmentId,
            SourceOrderId = order.Id,
            ServiceSessionId = attempt.ServiceSessionId,
            ClientOperationId = Guid.NewGuid(),
            ActorUserId = actorId,
            ActorRole = "Admin",
            ExpectedOrderVersion = order.Version,
            ExpectedAccountRevision = 2,
            Currency = "CHF",
            CreditMinor = includeManualSibling ? 200 : 100,
            RefundMinor = includeManualSibling ? 200 : 100,
            UnpaidWaivedMinor = 0,
            RequestHash = new string('c', 64),
            SnapshotJson = "{}",
            State = OrderAmendmentResolutionOperationState.Processing,
            StartedAt = now,
            CreatedBy = nameof(AccountCheckoutJournalTests)
        };
        var leg = new OrderAmendmentRefundLeg
        {
            Id = legId,
            OperationId = operation.Id,
            SourcePaymentId = payment.Id,
            AccountPaymentAttemptId = attempt.Id,
            Custody = OrderAmendmentRefundCustody.StripeDirect,
            State = OrderAmendmentRefundLegState.Processing,
            AmountMinor = 100,
            Currency = "CHF",
            FrozenScopesJson = "[]",
            ProviderAccountId = journal.ProviderAccountId,
            ProviderLiveMode = journal.ProviderLiveMode,
            ProviderChargeId = journal.ProviderChargeId,
            ProviderIntentId = journal.ProviderIntentId,
            CreatedBy = nameof(AccountCheckoutJournalTests)
        };
        var refundAttempt = new OrderAmendmentRefundAttempt
        {
            Id = refundAttemptId,
            RefundLegId = leg.Id,
            Sequence = 1,
            IdempotencyKey = $"amendment-refund:{refundAttemptId:N}",
            RequestedAt = now,
            CreatedBy = nameof(AccountCheckoutJournalTests)
        };
        leg.Attempts.Add(refundAttempt);
        operation.Legs.Add(leg);
        if (manualPaymentId.HasValue)
            operation.Legs.Add(new OrderAmendmentRefundLeg
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                SourcePaymentId = manualPaymentId.Value,
                Custody = OrderAmendmentRefundCustody.ManualTill,
                State = OrderAmendmentRefundLegState.Pending,
                AmountMinor = 100,
                Currency = "CHF",
                FrozenScopesJson = "[]",
                CreatedBy = nameof(AccountCheckoutJournalTests)
            });
        context.OrderAmendmentResolutionOperations.Add(operation);
        context.OrderAmendmentRefundEvidence.Add(new OrderAmendmentRefundEvidence
        {
            Id = Guid.NewGuid(),
            RefundLegId = leg.Id,
            RefundAttemptId = refundAttempt.Id,
            Sequence = 1,
            Kind = OrderAmendmentRefundEvidenceKind.ProviderRequest,
            State = OrderAmendmentRefundLegState.Processing,
            AmountMinor = leg.AmountMinor,
            Currency = leg.Currency,
            ActorUserId = actorId,
            ActorRole = operation.ActorRole,
            ObservedAt = now,
            ProviderChargeId = leg.ProviderChargeId,
            ProviderIntentId = leg.ProviderIntentId,
            ProviderAccountId = leg.ProviderAccountId,
            ProviderLiveMode = leg.ProviderLiveMode,
            EvidenceJson = "{}",
            CreatedBy = nameof(AccountCheckoutJournalTests)
        });
        await context.SaveChangesAsync();
        return new(operation.Id, leg.Id, refundAttempt.Id);
    }

    private static OrderAmendmentRefundEvidence NewRefundObservation(
        OrderAmendmentRefundLeg leg, OrderAmendmentRefundAttempt attempt, string refundId, int sequence) => new()
        {
            Id = Guid.NewGuid(),
            RefundLegId = leg.Id,
            RefundAttemptId = attempt.Id,
            Sequence = sequence,
            Kind = OrderAmendmentRefundEvidenceKind.ProviderObservation,
            State = OrderAmendmentRefundLegState.Succeeded,
            AmountMinor = leg.AmountMinor,
            Currency = leg.Currency,
            ActorUserId = Guid.Parse(TestAuthHandler.AdminUserId),
            ActorRole = "Admin",
            ObservedAt = Now,
            ProviderRefundId = refundId,
            ProviderRefundStatus = "succeeded",
            ProviderChargeId = leg.ProviderChargeId,
            ProviderIntentId = leg.ProviderIntentId,
            ProviderAccountId = leg.ProviderAccountId,
            ProviderLiveMode = leg.ProviderLiveMode,
            EvidenceJson = "{}",
            CreatedBy = nameof(AccountCheckoutJournalTests)
        };

    private static AccountCheckoutCanonicalEvidence CaptureEvidence(AccountCheckoutJournal journal,
        long refundedMinor = 0, IReadOnlyList<AmendmentRefundEvidence>? refunds = null)
    {
        var context = new AccountStripeContext(journal.ProviderAccountId, journal.ProviderLiveMode);
        var metadata = new Dictionary<string, string>
        {
            [AccountStripeCheckoutClient.AttemptMetadataKey] = journal.AttemptId.ToString("D"),
            [AccountStripeCheckoutClient.SchemaMetadataKey] = AccountStripeCheckoutClient.SchemaVersion
        };
        return new AccountCheckoutCanonicalEvidence(new AccountStripeSession
        {
            Id = journal.ProviderSessionId!,
            Context = context,
            Status = "complete",
            PaymentStatus = "paid",
            AmountMinor = journal.AmountMinor,
            Currency = journal.Currency,
            IntentId = journal.ProviderIntentId,
            Metadata = metadata,
            ClientReferenceId = journal.AttemptId.ToString("D")
        }, new AccountStripeIntent
        {
            Id = journal.ProviderIntentId!,
            Context = context,
            Status = "succeeded",
            AmountMinor = journal.AmountMinor,
            ReceivedMinor = journal.AmountMinor,
            Currency = journal.Currency,
            ChargeId = journal.ProviderChargeId,
            Metadata = metadata
        }, new AccountStripeCharge
        {
            Id = journal.ProviderChargeId!,
            Context = context,
            IntentId = journal.ProviderIntentId!,
            Status = "succeeded",
            AmountMinor = journal.AmountMinor,
            CapturedMinor = journal.AmountMinor,
            RefundedMinor = refundedMinor,
            Currency = journal.Currency,
            Paid = true,
            Captured = true,
            Disputed = false
        })
        { Refunds = refunds ?? [] };
    }

    private static AmendmentRefundEvidence ProviderRefund(
        RefundFixtureIds ids, string refundId, string status = "succeeded") => new(
        refundId, "ch_posting", "pi_posting", 100, "chf", status,
        new AmendmentRefundProviderContext("acct_journal", false), new Dictionary<string, string>
        {
            [StripeOrderAmendmentRefundProvider.SchemaKey] = StripeOrderAmendmentRefundProvider.SchemaVersion,
            [StripeOrderAmendmentRefundProvider.OperationKey] = ids.OperationId.ToString("D"),
            [StripeOrderAmendmentRefundProvider.LegKey] = ids.LegId.ToString("D"),
            [StripeOrderAmendmentRefundProvider.AttemptKey] = ids.AttemptId.ToString("D")
        });

    private sealed record RefundFixtureIds(Guid OperationId, Guid LegId, Guid AttemptId);
}
