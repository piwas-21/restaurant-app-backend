using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class OrderAmendmentRefundProviderAdoptionCompatibilityTests
{
    [Fact]
    public void Unrecorded_refund_adoption_requires_original_canonical_metadata_strings()
    {
        var operationId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var legId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var attemptId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
        var context = new AmendmentRefundProviderContext(nameof(OrderAmendmentRefundProviderAdoptionCompatibilityTests), false);
        var charge = $"charge-{operationId:N}";
        var intent = $"intent-{operationId:N}";
        var metadata = new Dictionary<string, string>
        {
            [StripeOrderAmendmentRefundProvider.SchemaKey] = StripeOrderAmendmentRefundProvider.SchemaVersion,
            [StripeOrderAmendmentRefundProvider.OperationKey] = operationId.ToString("D"),
            [StripeOrderAmendmentRefundProvider.LegKey] = legId.ToString("D"),
            [StripeOrderAmendmentRefundProvider.AttemptKey] = attemptId.ToString("D")
        };
        var refund = new AmendmentRefundEvidence($"refund-{operationId:N}", charge, intent,
            100, "CHF", "succeeded", context, metadata);
        var correlation = new RefundProviderCorrelation(new Dictionary<Guid, Guid>(),
            new Dictionary<Guid, Guid>(), operationId, legId, attemptId);

        Assert.Equal(100, OrderAmendmentRefundProviderProof.RequireCanonicalHistory(
            [], [refund], context, charge, intent, "CHF", correlation));

        var uppercase = new Dictionary<string, string>(metadata)
        {
            [StripeOrderAmendmentRefundProvider.OperationKey] = operationId.ToString("D").ToUpperInvariant()
        };
        Assert.NotEqual(metadata[StripeOrderAmendmentRefundProvider.OperationKey],
            uppercase[StripeOrderAmendmentRefundProvider.OperationKey]);
        Assert.Throws<ConflictException>(() => OrderAmendmentRefundProviderProof.RequireCanonicalHistory(
            [], [refund with { Metadata = uppercase }], context, charge, intent, "CHF", correlation));
    }
}
