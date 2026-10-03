using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.IntegrationTests.Features.Payments;

public sealed partial class AccountCheckoutJournalTests
{
    [Fact]
    public async Task Clean_capture_uses_the_configurable_twenty_four_hour_default_fallback()
    {
        var journal = await VerifyCapture();
        var settings = new AccountCheckoutSettings();
        settings.SettledReconciliationIntervalHours.Should().Be(24);

        await using (var context = DatabaseFixture.CreateContext())
        {
            await Poster(context).PostAsync(journal, CancellationToken.None);
            await new AccountCheckoutLeaseStore(context, Options.Create(settings), new FixedClock())
                .FinishAsync(journal.AttemptId, journal.LeaseId!.Value, null, CancellationToken.None);
        }

        await using var readback = DatabaseFixture.CreateContext();
        var saved = await readback.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == journal.AttemptId);
        var attempt = await readback.AccountPaymentAttempts.SingleAsync(value => value.Id == journal.AttemptId);
        attempt.State.Should().Be(AccountPaymentState.Captured);
        saved.ReconciliationRequired.Should().BeFalse();
        // PostgreSQL stores microseconds; the fixture clock deliberately adds 34567 ticks.
        saved.NextReconcileAt.Should().Be(Now.AddHours(24).AddTicks(34560));
        (await new AccountCheckoutLeaseStore(readback, Options.Create(settings), new FixedClock())
            .ReadDueAsync(CancellationToken.None)).Should().NotContain(journal.AttemptId);
        (await new AccountCheckoutLeaseStore(readback, Options.Create(settings), new FixedClock())
            .AcquireAsync(journal.AttemptId, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Webhook_arriving_during_a_lease_survives_finish_and_is_consumed_by_the_next_canonical_read()
    {
        var journal = await VerifyCapture();
        var settings = new AccountCheckoutSettings();

        await using (var context = DatabaseFixture.CreateContext())
        {
            var store = new AccountCheckoutLeaseStore(context, Options.Create(settings), new FixedClock());
            var scheduled = await store.ScheduleWebhookWakeupAsync(
                new AccountCheckoutWebhookReferences(journal.AttemptId, null, null, null),
                new AccountStripeContext("acct_journal", false), CancellationToken.None);
            scheduled.Should().BeTrue();
            await Poster(context).PostAsync(journal, CancellationToken.None);
            await store.FinishAsync(journal.AttemptId, journal.LeaseId!.Value, null, CancellationToken.None);

            var due = await store.ReadDueAsync(CancellationToken.None);
            due.Should().Contain(journal.AttemptId);
            var nextLease = await store.AcquireAsync(journal.AttemptId, CancellationToken.None);
            nextLease.Should().NotBeNull();
            nextLease!.WebhookWakeupPending.Should().BeFalse();
            await store.FinishAsync(journal.AttemptId, nextLease.LeaseId!.Value, null, CancellationToken.None);
        }

        await using var readback = DatabaseFixture.CreateContext();
        var saved = await readback.AccountCheckoutJournals.SingleAsync(value => value.AttemptId == journal.AttemptId);
        saved.NextReconcileAt.Should().Be(Now.AddHours(24).AddTicks(34560));
        saved.WebhookWakeupPending.Should().BeFalse();
    }

    [Fact]
    public async Task In_flight_unpaid_checkout_keeps_the_fast_thirty_second_poll()
    {
        var seed = await Seed();
        AccountCheckoutJournal journal;
        await using (var context = DatabaseFixture.CreateContext())
        {
            journal = await Store(context, seed.Actor, Provider().Object, Mock.Of<IGuestAccountPaymentPolicy>())
                .FreezeGuestAsync(seed.SessionId, seed.OperationId, 1,
                    "participant-credential", Credential, CancellationToken.None);
            var attemptId = await context.AccountPaymentAttempts.Select(value => value.Id).SingleAsync();
            var acquired = await new AccountCheckoutLeaseStore(context, Options.Create(new AccountCheckoutSettings()),
                new FixedClock()).AcquireAsync(attemptId, CancellationToken.None);
            acquired.Should().NotBeNull();
            journal = acquired!;
            await new AccountCheckoutLeaseStore(context, Options.Create(new AccountCheckoutSettings()), new FixedClock())
                .FinishAsync(attemptId, journal.LeaseId!.Value, null, CancellationToken.None);
        }

        await using var readback = DatabaseFixture.CreateContext();
        var saved = await readback.AccountCheckoutJournals.SingleAsync();
        saved.NextReconcileAt.Should().Be(Now.AddSeconds(30).AddTicks(34560));
        saved.ReconciliationRequired.Should().BeFalse();
    }

    [Fact]
    public async Task Signed_wakeup_reopens_a_released_attempt_for_canonical_late_capture_review()
    {
        var seed = await Seed();
        Guid attemptId;
        await using (var context = DatabaseFixture.CreateContext())
        {
            var journal = await Store(context, seed.Actor, Provider().Object, Mock.Of<IGuestAccountPaymentPolicy>())
                .FreezeGuestAsync(seed.SessionId, seed.OperationId, 1,
                    "participant-credential", Credential, CancellationToken.None);
            attemptId = journal.AttemptId;
            var attempt = await context.AccountPaymentAttempts.SingleAsync(value => value.Id == attemptId);
            attempt.State = AccountPaymentState.Released;
            attempt.CompletedAt = Now;
            journal.NextReconcileAt = Now.AddDays(1);
            await context.SaveChangesAsync();

            var store = new AccountCheckoutLeaseStore(context, Options.Create(new AccountCheckoutSettings()), new FixedClock());
            var scheduled = await store.ScheduleWebhookWakeupAsync(
                new AccountCheckoutWebhookReferences(attemptId, null, null, null),
                new AccountStripeContext("acct_journal", false), CancellationToken.None);
            scheduled.Should().BeTrue();
            (await store.ReadDueAsync(CancellationToken.None)).Should().Contain(attemptId);
        }

        await using var readback = DatabaseFixture.CreateContext();
        var acquired = await new AccountCheckoutLeaseStore(readback, Options.Create(new AccountCheckoutSettings()),
            new FixedClock()).AcquireAsync(attemptId, CancellationToken.None);
        acquired.Should().NotBeNull();
        acquired!.WebhookWakeupPending.Should().BeFalse();
        (await readback.AccountPaymentAttempts.SingleAsync(value => value.Id == attemptId))
            .State.Should().Be(AccountPaymentState.Released);
    }
}
