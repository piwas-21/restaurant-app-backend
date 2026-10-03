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

        if (resolutions.Any(IsUnresolved))
            throw PendingResolution();
    }

    public static async Task AssertNoPendingSessionResolutionAsync(
        ApplicationDbContext context,
        Guid serviceSessionId,
        CancellationToken cancellationToken)
    {
        var resolutions = await context.Set<RestaurantSystem.Domain.Entities.OrderAmendment>()
            .AsNoTracking()
            .Where(amendment => amendment.ServiceSessionId == serviceSessionId
                && amendment.State == OrderAmendmentState.Committed)
            .Select(amendment => amendment.FinancialResolutionJson)
            .ToListAsync(cancellationToken);

        if (resolutions.Any(IsUnresolved))
            throw PendingResolution();
    }

    internal static bool IsUnresolved(string? json) =>
        string.IsNullOrWhiteSpace(json) || HasUnresolvedOutcome(json);

    internal static void AssertResolved(string? json)
    {
        if (IsUnresolved(json))
            throw PendingResolution();
    }

    private static bool HasUnresolvedOutcome(string json)
    {
        OrderAmendmentFinancialPreviewDto resolution;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !HasEnum<OrderAmendmentFinancialResolutionStatus>(root, "resolutionStatus")
                || !HasEnum<OrderAmendmentCreditState>(root, "creditState")
                || !HasEnum<OrderAmendmentLoyaltyState>(root, "loyaltyState")
                || !HasEnum<OrderAmendmentRefundState>(root, "refundState")
                || !HasInteger(root, "addedAmountMinor")
                || !HasInteger(root, "removedUnitValueMinor")
                || !HasInteger(root, "netAccountDeltaMinor")
                || !HasInteger(root, "potentialCreditMinor")
                || !HasCurrency(root))
                return true;

            resolution = OrderAmendmentJson.Deserialize<OrderAmendmentFinancialPreviewDto>(json);
        }
        catch (JsonException)
        {
            return true;
        }

        return resolution.AddedAmountMinor < 0
            || resolution.RemovedUnitValueMinor < 0
            || resolution.PotentialCreditMinor != resolution.RemovedUnitValueMinor
            || resolution.NetAccountDeltaMinor != resolution.AddedAmountMinor - resolution.RemovedUnitValueMinor
            || (resolution.ResolutionStatus == OrderAmendmentFinancialResolutionStatus.NotRequired
                && (resolution.CreditState != OrderAmendmentCreditState.None
                    || resolution.LoyaltyState != OrderAmendmentLoyaltyState.None
                    || resolution.RefundState != OrderAmendmentRefundState.None))
            || resolution.ResolutionStatus == OrderAmendmentFinancialResolutionStatus.Pending
            || resolution.CreditState == OrderAmendmentCreditState.PendingAllocationReview
            || resolution.LoyaltyState == OrderAmendmentLoyaltyState.PendingReview
            || resolution.RefundState is OrderAmendmentRefundState.PendingTillRefund
                or OrderAmendmentRefundState.GatewayRefundRequired
                or OrderAmendmentRefundState.CustodianReviewRequired;
    }

    private static bool HasEnum<TEnum>(JsonElement root, string name) where TEnum : struct, Enum =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text
        && Enum.TryParse<TEnum>(text, out var parsed)
        && Enum.IsDefined(parsed)
        && string.Equals(Enum.GetName(parsed), text, StringComparison.Ordinal);

    private static bool HasInteger(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out _);

    private static bool HasCurrency(JsonElement root) =>
        root.TryGetProperty("currency", out var value)
        && (value.ValueKind == JsonValueKind.Null
            || value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()));

    private static ConflictException PendingResolution() => new(
        "An amendment financial adjustment for this order is unresolved. Reconcile its credit, refund, and loyalty outcome before continuing.");
}
