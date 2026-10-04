using RestaurantSystem.Api.Features.OrderAmendments.Dtos;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

public interface IOrderAmendmentEligibilityService
{
    Task<OrderAmendmentEligibilityDto> GetAsync(Guid orderId, CancellationToken cancellationToken);
}
