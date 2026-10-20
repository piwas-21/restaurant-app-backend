namespace RestaurantSystem.Api.Features.Orders.Services;

internal static class CurrentStaffTicketLimits
{
    // A single ticket is an interactive read budget, never a truncated bill.
    internal const int MaximumMembers = 100;
    internal const int MaximumAmendments = 1000;
}
