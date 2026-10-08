using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed partial class OrderAmendmentResolutionService
{
    private async Task<OrderAmendmentLoyaltyResultDto?> ReadLoyaltyResultAsync(
        OrderAmendmentResolutionOperation operation, CancellationToken cancellationToken)
    {
        OrderAmendmentResolutionSnapshot snapshot;
        try { snapshot = OrderAmendmentJson.Deserialize<OrderAmendmentResolutionSnapshot>(operation.SnapshotJson); }
        catch (JsonException exception)
        { throw new ConflictException("The frozen loyalty resolution result is incomplete.", exception); }
        var plan = snapshot.LoyaltyPlan;
        if (plan?.SnapshotId is null)
            return null;
        if (!plan.EarningDisposition.HasValue || !plan.EarningRetired.HasValue)
        {
            var accepted = await context.OrderBillingSnapshots.AsNoTracking()
                .SingleOrDefaultAsync(value => value.OrderId == operation.SourceOrderId, cancellationToken)
                ?? throw new ConflictException("The accepted loyalty snapshot is unavailable.");
            var disposition = accepted.EffectiveEarningDisposition;
            var retired = await context.OrderBillingEarningRetirements.AsNoTracking()
                .AnyAsync(value => value.OrderId == operation.SourceOrderId
                    && value.SnapshotId == accepted.Id, cancellationToken);
            plan = plan with
            {
                EarningDisposition = plan.EarningDisposition ?? disposition,
                EarningRetired = plan.EarningRetired ?? retired
            };
        }

        var headers = await context.OrderAmendmentLoyaltyCompensations.AsNoTracking()
            .Where(value => value.OperationId == operation.Id)
            .OrderBy(value => value.Kind).ThenBy(value => value.OriginalTransactionId)
            .ToListAsync(cancellationToken);
        var headerIds = headers.Select(value => value.Id).ToArray();
        var reservations = headerIds.Length == 0 ? []
            : await context.OrderAmendmentLoyaltyReservations.AsNoTracking()
                .Where(value => value.OperationId == operation.Id)
                .ToListAsync(cancellationToken);
        var postings = headerIds.Length == 0 ? []
            : await context.OrderAmendmentLoyaltyCompensationPostings.AsNoTracking()
                .Where(value => headerIds.Contains(value.CompensationId))
                .ToListAsync(cancellationToken);
        var ownerIds = new[] { plan.EarningOwnerLinkId, plan.RedemptionOwnerLinkId }
            .Where(value => value.HasValue).Select(value => value!.Value)
            .Concat(headers.Select(value => value.OwnerLinkId)).Distinct().ToArray();
        var owners = ownerIds.Length == 0 ? []
            : await context.OrderBillingSnapshotOwnerLinks.AsNoTracking()
                .Where(value => value.OrderId == operation.SourceOrderId && ownerIds.Contains(value.Id))
                .ToListAsync(cancellationToken);
        var witnesses = await context.OrderBillingAwardWitnesses.AsNoTracking()
            .Where(value => value.OrderId == operation.SourceOrderId).Take(2).ToListAsync(cancellationToken);
        if (witnesses.Count > 1)
            throw new ConflictException("The source order has ambiguous award result evidence.");

        long? available = null;
        if (reservations.SingleOrDefault()?.State is OrderAmendmentLoyaltyReservationState.HeldShortfall
                or OrderAmendmentLoyaltyReservationState.Reserved)
        {
            var clawback = plan.Compensations.SingleOrDefault(value =>
                value.Kind == OrderAmendmentLoyaltyCompensationKind.EarnedClawback)
                ?? throw new ConflictException("The active reservation has no exact clawback plan.");
            available = await OrderAmendmentLoyaltyReservationEvidence.ReadAvailableBeforeReservationAsync(
                context, reservations.Single(), clawback.RequiredPoints, cancellationToken);
        }

        return OrderAmendmentLoyaltyResultFactory.Create(operation,
            new OrderAmendmentLoyaltyResultEvidence(plan, witnesses.SingleOrDefault(), owners,
                headers, reservations, postings, available));
    }
}
