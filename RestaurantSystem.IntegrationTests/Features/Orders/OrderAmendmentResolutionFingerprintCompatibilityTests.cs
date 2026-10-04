using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderAmendmentResolutionFingerprintCompatibilityTests
{
    [Fact]
    public void Request_hash_preserves_the_original_persisted_json_contract()
    {
        var request = new OrderAmendmentResolutionStartRequest
        {
            Quote = new OrderAmendmentResolutionQuoteRequest
            {
                ClientOperationId = Identity(4),
                ExpectedOrderVersion = 4,
                ExpectedAccountRevision = 3,
                Currency = "CHF",
                ManualRefunds = [new ManualRefundSelectionRequest { PaymentId = Identity(5), AmountMinor = 400 }]
            },
            QuoteHash = new string('a', 64),
            ExpiresAt = new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc)
        };

        var actual = OrderAmendmentResolutionFingerprint.RequestHash(Identity(1), Identity(2), Identity(3), request);

        // SHA-256 of the original fixed request JSON, independently derived before the property rename.
        Assert.Equal("4be9a22465b81ad9c169d570aac6d39b0fc0e1434a437e3a36586c6afd7c365e", actual); // pragma: allowlist secret -- fixed SHA-256 compatibility oracle
    }

    private static Guid Identity(int suffix) => Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}");
}
