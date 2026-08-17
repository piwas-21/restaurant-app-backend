using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// The ledger that makes "send this mail at most once" true (EMAIL-SPEC-TENANT-APP GAP-12).
///
/// <para>
/// Run against the real database on purpose: the rule is a UNIQUE index, not a code path. Two
/// callers — the order handler and a guest's still-open tab — can read "not sent yet" at the same
/// instant, and only the database can arbitrate between them. A test with a faked store would pass
/// against an implementation that has no index at all.
/// </para>
/// </summary>
[Collection("Database Lane 3")]
public class OutboundEmailLedgerTests : IntegrationTestBase
{
    private const string EmailType = OutboundEmailTypes.OrderReceived;

    public OutboundEmailLedgerTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    [Fact]
    public async Task A_second_claim_on_the_same_mail_is_refused()
    {
        var ledger = Ledger();
        var entityId = Guid.NewGuid();

        (await ledger.TryClaimAsync(EmailType, entityId)).Should().BeTrue();
        (await ledger.TryClaimAsync(EmailType, entityId)).Should().BeFalse(
            "the mail is already sent or in flight");
    }

    /// <summary>
    /// Concurrency is the case the index exists for. Twenty simultaneous claimants, one winner —
    /// a read-then-write implementation lets several through here.
    /// </summary>
    [Fact]
    public async Task Only_one_of_many_simultaneous_claimants_wins()
    {
        var ledger = Ledger();
        var entityId = Guid.NewGuid();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => ledger.TryClaimAsync(EmailType, entityId)));

        results.Count(won => won).Should().Be(1);
    }

    /// <summary>
    /// A send that throws gives its claim back, or the mail becomes permanently unsendable — which
    /// is the very failure mode GAP-11 is about.
    /// </summary>
    [Fact]
    public async Task A_released_claim_can_be_taken_again()
    {
        var ledger = Ledger();
        var entityId = Guid.NewGuid();

        await ledger.TryClaimAsync(EmailType, entityId);
        await ledger.ReleaseAsync(EmailType, entityId);

        (await ledger.TryClaimAsync(EmailType, entityId)).Should().BeTrue();
    }

    /// <summary>
    /// The dangerous direction of the same rule: releasing a claim whose mail DID go out would let
    /// the next caller send the guest a duplicate.
    /// </summary>
    [Fact]
    public async Task A_sent_claim_survives_a_release()
    {
        var ledger = Ledger();
        var entityId = Guid.NewGuid();

        await ledger.TryClaimAsync(EmailType, entityId);
        await ledger.MarkSentAsync(EmailType, entityId);
        await ledger.ReleaseAsync(EmailType, entityId);

        (await ledger.TryClaimAsync(EmailType, entityId)).Should().BeFalse();
        (await ClaimAsync(entityId))!.SentAt.Should().NotBeNull();
    }

    /// <summary>
    /// A process that dies between claiming and sending must not silence the restaurant's order
    /// alert for good — after the staleness window the claim can be taken over. Simulated by aging
    /// the row, since the alternative is a 15-minute test.
    /// </summary>
    [Fact]
    public async Task A_claim_whose_sender_never_reported_back_can_be_taken_over()
    {
        var ledger = Ledger();
        var entityId = Guid.NewGuid();

        await ledger.TryClaimAsync(EmailType, entityId);
        await AgeClaimAsync(entityId, TimeSpan.FromHours(1));

        (await ledger.TryClaimAsync(EmailType, entityId)).Should().BeTrue();
    }

    /// <summary>
    /// The take-over is a hand-over, not a free-for-all: it moves the claim's clock forward in the
    /// same UPDATE whose row count decides the winner, so a second claimant arriving right behind
    /// the first is refused. This single statement is the subtlest part of the file.
    /// </summary>
    [Fact]
    public async Task A_take_over_re_arms_the_claim_against_the_next_caller()
    {
        var ledger = Ledger();
        var entityId = Guid.NewGuid();

        await ledger.TryClaimAsync(EmailType, entityId);
        await AgeClaimAsync(entityId, TimeSpan.FromHours(1));

        (await ledger.TryClaimAsync(EmailType, entityId)).Should().BeTrue();
        (await ledger.TryClaimAsync(EmailType, entityId)).Should().BeFalse(
            "the take-over reset the staleness clock; the mail is in flight again");
    }

    /// <summary>The take-over must not resurrect a mail that actually went out.</summary>
    [Fact]
    public async Task An_old_but_sent_claim_is_never_taken_over()
    {
        var ledger = Ledger();
        var entityId = Guid.NewGuid();

        await ledger.TryClaimAsync(EmailType, entityId);
        await ledger.MarkSentAsync(EmailType, entityId);
        await AgeClaimAsync(entityId, TimeSpan.FromHours(1));

        (await ledger.TryClaimAsync(EmailType, entityId)).Should().BeFalse();
    }

    /// <summary>Different mails about the same order are independent claims.</summary>
    [Fact]
    public async Task The_claim_is_per_mail_type_not_per_entity()
    {
        var ledger = Ledger();
        var entityId = Guid.NewGuid();

        (await ledger.TryClaimAsync(OutboundEmailTypes.OrderReceived, entityId)).Should().BeTrue();
        (await ledger.TryClaimAsync(OutboundEmailTypes.OrderAdminAlert, entityId)).Should().BeTrue();
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private IOutboundEmailLedger Ledger() => Factory.Services.GetRequiredService<IOutboundEmailLedger>();

    private async Task<OutboundEmail?> ClaimAsync(Guid entityId)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await context.OutboundEmails.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EntityId == entityId);
    }

    private async Task AgeClaimAsync(Guid entityId, TimeSpan by)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.OutboundEmails
            .Where(e => e.EntityId == entityId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedAt, e => e.CreatedAt - by));
    }
}
