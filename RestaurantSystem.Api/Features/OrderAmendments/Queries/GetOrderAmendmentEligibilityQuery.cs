using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;

namespace RestaurantSystem.Api.Features.OrderAmendments.Queries;

public sealed record GetOrderAmendmentEligibilityQuery(Guid OrderId)
    : IQuery<ApiResponse<OrderAmendmentEligibilityDto>>;

public sealed class GetOrderAmendmentEligibilityQueryHandler
    : IQueryHandler<GetOrderAmendmentEligibilityQuery, ApiResponse<OrderAmendmentEligibilityDto>>
{
    private readonly IOrderAmendmentEligibilityService _eligibility;

    public GetOrderAmendmentEligibilityQueryHandler(IOrderAmendmentEligibilityService eligibility) =>
        _eligibility = eligibility;

    public async Task<ApiResponse<OrderAmendmentEligibilityDto>> Handle(
        GetOrderAmendmentEligibilityQuery query, CancellationToken cancellationToken) =>
        ApiResponse<OrderAmendmentEligibilityDto>.SuccessWithData(
            await _eligibility.GetAsync(query.OrderId, cancellationToken),
            "Order amendment eligibility loaded");
}
