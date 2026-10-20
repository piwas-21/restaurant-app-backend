namespace RestaurantSystem.Api.Features.Orders.Services.CustomerCheckout;

internal static class CustomerCheckoutLimits
{
    // Match the installed basket presentation budget; never truncate a purchase review.
    internal const int MaximumLines = 1000;
    internal const int MaximumDepth = 8;
    // A receipt refuses an oversized frozen recipe rather than truncating customer choices.
    internal const int MaximumIngredientLines = 10000;
}
