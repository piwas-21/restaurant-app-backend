namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

internal static class CustomerCheckoutLimits
{
    // Match the installed basket presentation budget; never truncate a purchase review.
    internal const int MaximumLines = 1000;
    internal const int MaximumDepth = 8;
}
