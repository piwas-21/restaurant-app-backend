using RestaurantSystem.Api.Features.OrderAmendments.Dtos;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public interface IOrderAmendmentRefundProvider
{
    AmendmentRefundProviderContext ReadContext();

    Task<AmendmentRefundEvidence> CreateAsync(
        AmendmentRefundRequest request, CancellationToken cancellationToken);

    Task<IReadOnlyList<AmendmentRefundEvidence>> ListForChargeAsync(
        string chargeId, CancellationToken cancellationToken);
}
