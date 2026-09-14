using RestaurantSystem.Api.Abstraction.Messaging;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.Orders.Queries.GetPaymentOperationQuery;

/// <param name="OrderId">The order to reconcile.</param>
/// <param name="OperationId">The client operation key. No payment payload is accepted for a lookup.</param>
public record GetPaymentOperationQuery(Guid OrderId, Guid OperationId)
    : IQuery<ApiResponse<PaymentOperationLookupDto>>;

public sealed class GetPaymentOperationQueryHandler
    : IQueryHandler<GetPaymentOperationQuery, ApiResponse<PaymentOperationLookupDto>>
{
    private readonly IOrderPaymentReplayResolver _replayResolver;
    private readonly IOrderMappingService _mappingService;

    public GetPaymentOperationQueryHandler(
        IOrderPaymentReplayResolver replayResolver,
        IOrderMappingService mappingService)
    {
        _replayResolver = replayResolver;
        _mappingService = mappingService;
    }

    public async Task<ApiResponse<PaymentOperationLookupDto>> Handle(
        GetPaymentOperationQuery query, CancellationToken cancellationToken)
    {
        var resolution = await _replayResolver.ResolveOperationAsync(
            query.OrderId, query.OperationId, cancellationToken);

        var orderDto = resolution.Order is null
            ? null
            : await _mappingService.MapToOrderDtoAsync(resolution.Order, cancellationToken);
        var paymentDto = resolution.Payment is null
            ? null
            : _mappingService.MapToOrderPaymentDto(resolution.Payment);
        var committed = resolution.Payment is not null;

        var result = new PaymentOperationLookupDto
        {
            OperationId = query.OperationId,
            Status = committed ? PaymentOperationLookupStatus.Committed : PaymentOperationLookupStatus.Unknown,
            Payment = paymentDto,
            Order = orderDto
        };

        // Unknown is a valid reconciliation answer, not an HTTP failure. The order, when it
        // exists, is still returned from the server's full read so the caller can replace stale
        // local state before deciding whether to try a new operation.
        return ApiResponse<PaymentOperationLookupDto>.SuccessWithData(
            result,
            committed ? "Payment operation committed" : "Payment operation is unknown");
    }
}
