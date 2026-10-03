using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.Payments.Interfaces;
using RestaurantSystem.Api.Settings;
using Microsoft.Extensions.Options;
using Stripe;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public sealed class StripeOrderAmendmentRefundProvider(
    IStripeGateway gateway,
    IOptions<StripeSettings> options,
    IOptions<OrderAmendmentResolutionSettings> resolutionSettings)
    : IOrderAmendmentRefundProvider
{
    internal const string SchemaKey = "sofra_amendment_refund_schema";
    internal const string SchemaVersion = "amendment-refund-v1";
    internal const string OperationKey = "amendment_resolution_operation";
    internal const string LegKey = "amendment_refund_leg";
    internal const string AttemptKey = "amendment_refund_attempt";

    public AmendmentRefundProviderContext ReadContext()
    {
        if (!gateway.IsConfigured)
            throw new BadRequestException("The connected payment account is unavailable.");
        var key = options.Value.PlatformApiKey;
        var live = key.StartsWith("sk_live_", StringComparison.Ordinal)
            || key.StartsWith("rk_live_", StringComparison.Ordinal);
        var test = key.StartsWith("sk_test_", StringComparison.Ordinal)
            || key.StartsWith("rk_test_", StringComparison.Ordinal);
        if (!live && !test)
            throw new BadRequestException("The connected payment environment is unavailable.");
        return new AmendmentRefundProviderContext(gateway.ConnectedAccountId, live);
    }

    public async Task<AmendmentRefundEvidence> CreateAsync(
        AmendmentRefundRequest request, CancellationToken cancellationToken)
    {
        if (request.AmountMinor <= 0 || string.IsNullOrWhiteSpace(request.ChargeId)
            || string.IsNullOrWhiteSpace(request.IntentId) || request.Currency.Length != 3
            || request.IdempotencyKey.Length > 255 || request.Metadata.Count != 4)
            throw new BadRequestException("The frozen refund request is incomplete.");
        var refund = await new RefundService(gateway.Client).CreateAsync(new RefundCreateOptions
        {
            Charge = request.ChargeId,
            Amount = request.AmountMinor,
            Metadata = request.Metadata.ToDictionary(value => value.Key, value => value.Value,
                StringComparer.Ordinal)
        }, gateway.BuildRequestOptions(request.IdempotencyKey), cancellationToken);
        return Map(refund);
    }

    public async Task<IReadOnlyList<AmendmentRefundEvidence>> ListForChargeAsync(
        string chargeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chargeId))
            throw new BadRequestException("The frozen charge identity is unavailable.");
        var service = new RefundService(gateway.Client);
        var pageOptions = new RefundListOptions
        {
            Charge = chargeId,
            Limit = resolutionSettings.Value.ProviderRefundPageSize
        };
        var results = new List<AmendmentRefundEvidence>();
        for (var page = 0; page < resolutionSettings.Value.MaximumProviderRefundPages; page++)
        {
            var response = await service.ListAsync(pageOptions, gateway.BuildRequestOptions(), cancellationToken);
            results.AddRange(response.Data.Select(Map));
            if (!response.HasMore)
                return results;
            if (response.Data.Count == 0)
                break;
            pageOptions.StartingAfter = response.Data[^1].Id;
        }
        throw new ConflictException("Provider refund history exceeds the bounded reconciliation window.");
    }

    private AmendmentRefundEvidence Map(Refund refund) => new(
        refund.Id ?? string.Empty,
        refund.ChargeId ?? string.Empty,
        refund.PaymentIntentId ?? string.Empty,
        refund.Amount,
        refund.Currency ?? string.Empty,
        refund.Status ?? string.Empty,
        new AmendmentRefundProviderContext(gateway.ConnectedAccountId, ReadContext().LiveMode),
        refund.Metadata ?? new Dictionary<string, string>());
}
