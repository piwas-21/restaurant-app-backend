using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed partial class AccountCheckoutJournalTests
{
    [Fact]
    public async Task Verified_money_posts_once_even_when_notifications_fail()
    {
        var journal = await VerifyCapture();
        await using (var context = DatabaseFixture.CreateContext())
            await Poster(context).PostAsync(journal, CancellationToken.None);
        await using (var context = DatabaseFixture.CreateContext())
            await Poster(context).PostAsync(journal, CancellationToken.None);
        await using var readback = DatabaseFixture.CreateContext();
        var attempt = await readback.AccountPaymentAttempts.Include(value => value.Allocations).SingleAsync();
        attempt.State.Should().Be(AccountPaymentState.Captured);
        attempt.Allocations.Should().OnlyContain(value => value.OrderPaymentId.HasValue);
        var payment = await readback.OrderPayments.SingleAsync();
        payment.Amount.Should().Be(3.34m);
        payment.TransactionId.Should().Be("ch_posting");
        payment.PaymentGateway.Should().Be("Stripe");
        var saved = await readback.AccountCheckoutJournals.SingleAsync();
        saved.ProviderCapturedMinor.Should().Be(334);
        saved.ReconciliationRequired.Should().BeFalse();
        var account = await new AccountDebtSnapshotReader(readback).ReadAsync(attempt.ServiceSessionId, CancellationToken.None);
        account.Debt.OutstandingMinor.Should().Be(0);
        account.Debt.ReservedMinor.Should().Be(0);
    }

    [Fact]
    public async Task Failed_card_charge_can_be_replaced_by_the_successful_retry_charge()
    {
        var journal = await VerifyCapture(observedFailedCharge: true);
        await using (var context = DatabaseFixture.CreateContext())
            await Poster(context).PostAsync(journal, CancellationToken.None);
        await using var readback = DatabaseFixture.CreateContext();
        (await readback.AccountCheckoutJournals.SingleAsync()).ProviderChargeId.Should().Be("ch_posting");
        (await readback.OrderPayments.SingleAsync()).TransactionId.Should().Be("ch_posting");
        (await readback.AccountPaymentAttempts.SingleAsync()).State.Should().Be(AccountPaymentState.Captured);
    }

    [Fact]
    public async Task Loyalty_runs_after_the_money_transaction_is_disposed()
    {
        var journal = await VerifyCapture();
        await using var context = DatabaseFixture.CreateContext();
        var fidelity = new Mock<IOrderFidelityCoordinator>();
        var ranWithoutTransaction = false;
        fidelity.Setup(value => value.AwardEarnedPointsAsync(It.IsAny<Order>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>())).Returns(() =>
            {
                ranWithoutTransaction = context.Database.CurrentTransaction is null;
                return Task.CompletedTask;
            });
        await Poster(context, fidelity: fidelity.Object).PostAsync(journal, CancellationToken.None);
        fidelity.Verify(value => value.AwardEarnedPointsAsync(It.IsAny<Order>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        ranWithoutTransaction.Should().BeTrue();
    }

    [Fact]
    public async Task Allocation_failure_keeps_verified_money_durable_and_blocks_close()
    {
        var journal = await VerifyCapture();
        await using (var context = DatabaseFixture.CreateContext())
        {
            var writer = new Mock<IAccountPaymentCaptureWriter>();
            writer.Setup(value => value.RecordVerifiedProviderAsync(It.IsAny<AccountPaymentAttempt>(),
                It.IsAny<AccountCheckoutJournal>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ConflictException("Scope changed"));
            var post = () => Poster(context, writer.Object).PostAsync(journal, CancellationToken.None);
            await post.Should().ThrowAsync<ConflictException>();
        }
        await using var readback = DatabaseFixture.CreateContext();
        (await readback.OrderPayments.CountAsync()).Should().Be(0);
        var saved = await readback.AccountCheckoutJournals.SingleAsync();
        saved.ProviderCapturedMinor.Should().Be(334);
        saved.ReconciliationRequired.Should().BeTrue();
        var attempt = await readback.AccountPaymentAttempts.SingleAsync();
        attempt.State.Should().Be(AccountPaymentState.Processing);
        var close = () => AccountPaymentCloseGuard.RequireClosableAsync(readback,
            attempt.ServiceSessionId, null, CancellationToken.None);
        await close.Should().ThrowAsync<ConflictException>();
        var receipt = await new AccountPaymentReceiptReader(readback, new FixedClock())
            .ReadAsync(attempt.Id, Credential, CancellationToken.None);
        receipt.ReceivedMinor.Should().Be(334);
        receipt.ReconciliationRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Provider_money_never_attaches_to_the_next_party_at_the_same_table()
    {
        var journal = await VerifyCapture();
        Guid nextVisit;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var oldVisit = await context.TableServiceSessions.SingleAsync();
            oldVisit.Status = TableServiceSessionStatus.Closed;
            nextVisit = Guid.NewGuid();
            context.TableServiceSessions.Add(new TableServiceSession
            {
                Id = nextVisit,
                TableNumber = oldVisit.TableNumber,
                Currency = "CHF",
                OpenedAt = Now,
                CreatedBy = "test"
            });
            await context.SaveChangesAsync();
        }
        await using (var context = DatabaseFixture.CreateContext())
            await Poster(context).PostAsync(journal, CancellationToken.None);
        await using var readback = DatabaseFixture.CreateContext();
        var attempt = await readback.AccountPaymentAttempts.SingleAsync();
        attempt.ServiceSessionId.Should().NotBe(nextVisit);
        var order = await readback.Orders.Include(value => value.Payments).SingleAsync();
        order.ServiceSessionId.Should().Be(attempt.ServiceSessionId);
        order.Payments.Should().ContainSingle().Which.Amount.Should().Be(3.34m);
        (await readback.Orders.AnyAsync(value => value.ServiceSessionId == nextVisit)).Should().BeFalse();
        (await readback.TableServiceSessions.SingleAsync(value => value.Id == nextVisit))
            .AccountRevision.Should().Be(1);
    }

    private async Task<AccountCheckoutJournal> VerifyCapture(
        bool observedFailedCharge = false, bool sharedScope = false)
    {
        var seed = await Seed(sharedScope);
        await using (var context = DatabaseFixture.CreateContext())
            await Store(context, seed.Actor, Provider().Object, Mock.Of<IGuestAccountPaymentPolicy>())
                .FreezeGuestAsync(seed.SessionId, seed.OperationId, 1,
                    "participant-credential", Credential, CancellationToken.None);
        AccountCheckoutJournal journal;
        await using (var context = DatabaseFixture.CreateContext())
            journal = await new AccountCheckoutLeaseStore(context, Options.Create(new AccountCheckoutSettings()),
                new FixedClock()).AcquireAsync(
                    (await context.AccountPaymentAttempts.SingleAsync()).Id, CancellationToken.None)
                ?? throw new InvalidOperationException("Fixture lease was not acquired.");
        var metadata = new Dictionary<string, string>
        {
            [AccountStripeCheckoutClient.AttemptMetadataKey] = journal.AttemptId.ToString("D"),
            [AccountStripeCheckoutClient.SchemaMetadataKey] = AccountStripeCheckoutClient.SchemaVersion
        };
        var providerContext = new AccountStripeContext("acct_journal", false);
        var evidence = new AccountCheckoutCanonicalEvidence(new AccountStripeSession
        {
            Id = "cs_posting",
            Context = providerContext,
            Status = "complete",
            PaymentStatus = "paid",
            AmountMinor = 334,
            Currency = "CHF",
            IntentId = "pi_posting",
            Metadata = metadata,
            ClientReferenceId = journal.AttemptId.ToString("D")
        }, new AccountStripeIntent
        {
            Id = "pi_posting",
            Context = providerContext,
            Status = "succeeded",
            AmountMinor = 334,
            ReceivedMinor = 334,
            Currency = "CHF",
            ChargeId = "ch_posting",
            Metadata = metadata
        }, new AccountStripeCharge
        {
            Id = "ch_posting",
            Context = providerContext,
            IntentId = "pi_posting",
            Status = "succeeded",
            AmountMinor = 334,
            CapturedMinor = 334,
            RefundedMinor = 0,
            Currency = "CHF",
            Paid = true,
            Captured = true,
            Disputed = false
        });
        if (observedFailedCharge)
        {
            var failedEvidence = new AccountCheckoutCanonicalEvidence(evidence.Session with
            {
                Status = "open",
                PaymentStatus = "unpaid"
            }, evidence.Intent! with
            {
                Status = "requires_payment_method",
                ReceivedMinor = 0,
                ChargeId = "ch_failed"
            }, evidence.Charge! with
            {
                Id = "ch_failed",
                Status = "failed",
                CapturedMinor = 0,
                Paid = false,
                Captured = false
            });
            await using var failedContext = DatabaseFixture.CreateContext();
            journal = await new AccountCheckoutEvidenceWriter(failedContext,
                Options.Create(new AccountCheckoutSettings()), new FixedClock())
                .RecordAsync(journal, failedEvidence, CancellationToken.None);
            journal.ProviderCapturedMinor.Should().Be(0);
            journal.ProviderChargeId.Should().BeNull();
        }
        await using var writeContext = DatabaseFixture.CreateContext();
        return await new AccountCheckoutEvidenceWriter(writeContext, Options.Create(new AccountCheckoutSettings()),
            new FixedClock()).RecordAsync(journal, evidence, CancellationToken.None);
    }

    private static AccountCheckoutCapturePoster Poster(
        RestaurantSystem.Infrastructure.Persistence.ApplicationDbContext context, IAccountPaymentCaptureWriter? writer = null, IOrderFidelityCoordinator? fidelity = null)
    {
        var defaultFidelity = new Mock<IOrderFidelityCoordinator>();
        defaultFidelity.Setup(value => value.AwardEarnedPointsAsync(It.IsAny<Order>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var mapping = new Mock<IOrderMappingService>();
        mapping.Setup(value => value.MapToOrderDtoAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Notification unavailable"));
        writer ??= new AccountPaymentCaptureWriter(context, Mock.Of<ICurrentUserService>(), new FixedClock());
        return new(context, writer, fidelity ?? defaultFidelity.Object, mapping.Object, Mock.Of<IOrderEventService>(), new FixedClock(),
            NullLogger<AccountCheckoutCapturePoster>.Instance);
    }
}
