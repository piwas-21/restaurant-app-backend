using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

/// <summary>Prevents native payment mutations while a committed amendment still needs reconciliation.</summary>
public static class OrderAmendmentFinancialGuard
{
    public static async Task AssertNoPendingSourceResolutionAsync(
        ApplicationDbContext context,
        Guid sourceOrderId,
        CancellationToken cancellationToken)
    {
        var resolutions = await context.Set<RestaurantSystem.Domain.Entities.OrderAmendment>()
            .AsNoTracking()
            .Where(amendment => amendment.SourceOrderId == sourceOrderId
                && amendment.State == OrderAmendmentState.Committed)
            .Select(amendment => amendment.FinancialResolutionJson)
            .ToListAsync(cancellationToken);

        foreach (var json in resolutions)
        {
            if (string.IsNullOrWhiteSpace(json) || HasUnresolvedOutcome(json))
            {
                throw PendingResolution();
            }
        }
    }

    private static bool HasUnresolvedOutcome(string json)
    {
        OrderAmendmentFinancialPreviewDto resolution;
        try
        {
            resolution = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(json);
        }
        catch (JsonException)
        {
            return true;
        }

        return !Enum.IsDefined(resolution.ResolutionStatus)
            || !Enum.IsDefined(resolution.CreditState)
            || !Enum.IsDefined(resolution.LoyaltyState)
            || !Enum.IsDefined(resolution.RefundState)
            || resolution.ResolutionStatus == OrderAmendmentFinancialResolutionStatus.Pending
            || resolution.CreditState == OrderAmendmentCreditState.PendingAllocationReview
            || resolution.LoyaltyState == OrderAmendmentLoyaltyState.PendingReview
            || resolution.RefundState is OrderAmendmentRefundState.PendingTillRefund
                or OrderAmendmentRefundState.GatewayRefundRequired
                or OrderAmendmentRefundState.CustodianReviewRequired;
    }

    private static ConflictException PendingResolution() => new(
        "An amendment financial adjustment for this order is unresolved. Reconcile its credit, refund, and loyalty outcome before continuing.");
}
