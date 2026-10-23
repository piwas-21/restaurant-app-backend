using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.AccountPayments.Dtos;
using RestaurantSystem.Api.Features.Payments.Services;
using RestaurantSystem.Api.Features.TableGuestVisits.Services;
using RestaurantSystem.Api.Features.TableServiceSessions.Services;
using RestaurantSystem.Api.Features.User.Services;
using RestaurantSystem.Api.Settings;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Recheck the original private owner after all provider/post-commit work.</summary>
public sealed class CustomerTableCheckoutProjection(ApplicationDbContext context, ICustomerTablePaymentAccess access,
    ICustomerTablePaymentProofs proofs, ICustomerTablePaymentDecisions decisions, ICustomerTableCheckoutAuthority authority,
    IOptions<CustomerHostedCheckoutSettings> hosted,
    TimeProvider clock) : ICustomerTableCheckoutProjection
{
    public async Task<CustomerTableCheckoutDto> ReadAsync(string sessionId, CustomerTablePaymentProofDto proof,
        CustomerTableCheckoutIdentity original, string? participantToken, string? canonicalUrl, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new ConflictException("Native checkout status requires a clean context.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await TableServiceSessionRowLock.LoadAsync(context, original.Binding.ServiceSessionId, cancellationToken);
        if (original.Owner.CustomerId is { } customer)
            await CustomerAccountWriteGuard.AcquireAsync(context, customer, true, cancellationToken);
        if (access.Capture(sessionId, proof.SchemaVersion, proof.TenantId) != original.Owner
            || proofs.Require(proof, original.Owner) != original.Binding) throw Unavailable();
        var decision = await decisions.ReadAsync(original.Owner, proof, original.Binding, cancellationToken)
            ?? throw Unavailable();
        if (decision.AttemptId != original.AttemptId) throw Unavailable();
        var journal = decision.AttemptId is Guid attempt
            ? await context.AccountCheckoutJournals.AsNoTracking().SingleOrDefaultAsync(row => row.AttemptId == attempt, cancellationToken)
            : null;
        var (outcome, url) = await OutcomeAsync(decision, journal, original, proof, participantToken, canonicalUrl, cancellationToken);
        if (access.Capture(sessionId, proof.SchemaVersion, proof.TenantId) != original.Owner) throw Unavailable();
        await transaction.CommitAsync(cancellationToken);
        return new(1, proof.TenantId, proof.OperationId, original.Binding.ServiceSessionId, decision.AttemptId,
            outcome, original.Binding.AmountMinor, original.Binding.Currency, journal?.ExpiresAt, url);
    }

    private async Task<(string Outcome, string? Url)> OutcomeAsync(CustomerTablePaymentResolutionDto decision,
        RestaurantSystem.Domain.Entities.AccountCheckoutJournal? journal, CustomerTableCheckoutIdentity original,
        CustomerTablePaymentProofDto proof, string? participantToken, string? canonicalUrl, CancellationToken cancellationToken)
    {
        var outcome = decision.Outcome == CustomerTablePaymentOutcome.Stopped ? "stopped" : "pending";
        string? url = null;
        if (journal is not null)
        {
            CustomerTableCheckoutStore.RequireJournal(journal, proof);
            if (journal.AmountMinor != original.Binding.AmountMinor || journal.Currency != original.Binding.Currency)
                throw Unavailable();
            if (!journal.ReconciliationRequired && journal.LastVerifiedAt is not null)
                return await VerifiedOutcomeAsync(decision, journal, original, proof, participantToken, canonicalUrl, cancellationToken);
        }
        else if (decision.State == AccountPaymentState.Released) outcome = "stopped";
        else if (decision.State == AccountPaymentState.Reserved) outcome = "reserved";
        return (outcome, url);
    }

    private async Task<(string Outcome, string? Url)> VerifiedOutcomeAsync(CustomerTablePaymentResolutionDto decision,
        RestaurantSystem.Domain.Entities.AccountCheckoutJournal journal, CustomerTableCheckoutIdentity original,
        CustomerTablePaymentProofDto proof, string? participantToken, string? canonicalUrl, CancellationToken cancellationToken)
    {
        var outcome = "pending";
        string? url = null;
        if (decision.State == AccountPaymentState.Captured && journal.ProviderCapturedMinor == journal.AmountMinor
            && journal.ProviderRefundedMinor == 0 && journal.ProviderChargeId is not null)
        {
            // This independently verifies full tender/allocation accounting before a paid projection.
            await new AccountDebtSnapshotReader(context).ReadAsync(original.Binding.ServiceSessionId, cancellationToken);
            outcome = "paid";
        }
        else if (decision.State == AccountPaymentState.Released
            && journal.ProviderCapturedMinor == journal.ProviderRefundedMinor) outcome = "stopped";
        else if (decision.State == AccountPaymentState.Processing && journal.CancelRequestedAt is null
            && journal.ProviderCapturedMinor == 0 && clock.GetUtcNow().UtcDateTime < journal.ExpiresAt
            && TrustedHostedCheckoutUrl.IsAllowed(canonicalUrl, hosted.Value.HostedPageOrigins)
            && await authority.MayOpenAsync(original, proof, participantToken, cancellationToken))
        { outcome = "ready"; url = canonicalUrl; }
        return (outcome, url);
    }

    private static NotFoundException Unavailable() => new("The original native contribution is unavailable.");
}
