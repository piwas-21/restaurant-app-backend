using RestaurantSystem.Api.Features.Basket.Services.Operations;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Services.CustomerRounds;

internal static class CustomerTableRoundHashes
{
    internal const string Purpose = "RestaurantSystem.CustomerTableRound.v1";
    internal const long MaximumSafeRevision = 9_007_199_254_740_991;
    internal static bool IsDigest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    internal static string Owner(Guid sessionId, Guid? customerId) =>
        CustomerBasketOperationHash.Payload(new { Purpose, SessionId = sessionId, CustomerId = customerId });
}
