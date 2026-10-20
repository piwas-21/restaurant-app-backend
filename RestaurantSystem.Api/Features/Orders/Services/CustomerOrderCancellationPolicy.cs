using RestaurantSystem.Domain.Common.Constants;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Native customer delay rejection; never an authority to refund or cancel staff work.</summary>
public static class CustomerOrderCancellationPolicy
{
    // Ownership, current configuration, standalone/local membership and absence of financial work
    // must be verified by the server under the order lock. Missing evidence never grants access.
    public static bool CanDeclineDelay(OrderStatus status, string? confirmationFlow,
        bool verifiedLocalStandalone, bool verifiedFinancialClearance) =>
        status == OrderStatus.PendingApproval
        && string.Equals(confirmationFlow, OrderConfirmationFlows.Direct, StringComparison.Ordinal)
        && verifiedLocalStandalone
        && verifiedFinancialClearance;
}
