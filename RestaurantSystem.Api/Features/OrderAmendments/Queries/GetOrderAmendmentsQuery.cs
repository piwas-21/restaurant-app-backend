using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.OrderAmendments.Dtos;
using RestaurantSystem.Api.Features.OrderAmendments.Services;

namespace RestaurantSystem.Api.Features.OrderAmendments.Queries;

public sealed record GetOrderAmendmentsQuery(Guid OrderId)
    : IQuery<ApiResponse<IReadOnlyList<OrderAmendmentHistoryDto>>>;

public sealed class GetOrderAmendmentsQueryHandler
    : IQueryHandler<GetOrderAmendmentsQuery, ApiResponse<IReadOnlyList<OrderAmendmentHistoryDto>>>
{
    private readonly IOrderAmendmentQueryService _queries;

    public GetOrderAmendmentsQueryHandler(IOrderAmendmentQueryService queries) => _queries = queries;

    public async Task<ApiResponse<IReadOnlyList<OrderAmendmentHistoryDto>>> Handle(
        GetOrderAmendmentsQuery query, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<OrderAmendmentHistoryDto>>.SuccessWithData(
            await _queries.ListAsync(query.OrderId, cancellationToken), "Order amendment history loaded");
}

public sealed record GetOrderAmendmentOperationQuery(Guid OperationId)
    : IQuery<ApiResponse<OrderAmendmentOperationLookupDto>>;

public sealed class GetOrderAmendmentOperationQueryHandler
    : IQueryHandler<GetOrderAmendmentOperationQuery, ApiResponse<OrderAmendmentOperationLookupDto>>
{
    private readonly IOrderAmendmentQueryService _queries;

    public GetOrderAmendmentOperationQueryHandler(IOrderAmendmentQueryService queries) => _queries = queries;

    public async Task<ApiResponse<OrderAmendmentOperationLookupDto>> Handle(
        GetOrderAmendmentOperationQuery query, CancellationToken cancellationToken) =>
        ApiResponse<OrderAmendmentOperationLookupDto>.SuccessWithData(
            await _queries.LookupAsync(query.OperationId, cancellationToken), "Order amendment operation checked");
}
