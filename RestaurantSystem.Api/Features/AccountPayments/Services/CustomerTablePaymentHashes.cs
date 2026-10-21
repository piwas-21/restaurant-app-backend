using RestaurantSystem.Api.Features.Basket.Services.Operations;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

internal static class CustomerTablePaymentHashes
{
    internal const string Purpose = "RestaurantSystem.CustomerTablePayment.v1";
    internal const long MaximumExactInteger = 9_007_199_254_740_991;
    internal const int MaximumSegments = 5_000;
    internal static bool IsDigest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    internal static string Owner(Guid session, Guid? customer) =>
        CustomerBasketOperationHash.Payload(new { Purpose, SessionId = session, CustomerId = customer });
}
