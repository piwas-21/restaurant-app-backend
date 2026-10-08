using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.FidelityPoints.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.FidelityPoints.Services;

public partial class FidelityPointsService
{
    private async Task<FidelityPointsAwardResult> AwardWithoutAcceptedCandidateAsync(
        AwardOrderState order, OrderBillingSnapshot snapshot,
        List<FidelityPointsTransaction> existingAwards,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (order.FidelityPointsEarned != 0 || HasPartialEarningEvidence(snapshot))
            throw new ConflictException("The unevaluated earning snapshot contains partial or inconsistent rule evidence.");
        if (existingAwards.Count != 0)
            throw new ConflictException("An order without an accepted earning candidate has an earned ledger row.");

        var disposition = snapshot.EffectiveEarningDisposition;
        if (disposition == OrderBillingEarningDisposition.Evaluated)
            throw new ConflictException("An evaluated earning snapshot is missing its immutable candidate.");
        if (disposition is OrderBillingEarningDisposition.NoCustomerOwnerAtAcceptance
                or OrderBillingEarningDisposition.LoyaltyModuleDisabledAtAcceptance
            || await _context.OrderBillingEarningRetirements.AsNoTracking()
                .AnyAsync(value => value.OrderId == order.Id && value.SnapshotId == snapshot.Id,
                    cancellationToken))
        {
            await CommitOwnedTransactionAsync(transaction, cancellationToken);
            return new FidelityPointsAwardResult(
                FidelityPointsAwardDisposition.IneligibleAtAcceptance, null, null, 0, 0);
        }
        if (disposition != OrderBillingEarningDisposition.Unevaluated)
            throw new ConflictException("The frozen earning disposition is not recognized.");
        return Deferred(FidelityPointsAwardDeferralReason.CandidateUnevaluated);
    }
}
