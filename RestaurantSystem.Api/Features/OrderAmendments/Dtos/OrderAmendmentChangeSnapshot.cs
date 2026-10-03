using RestaurantSystem.Api.Features.Orders.Dtos;
using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.OrderAmendments.Dtos;

public sealed record OrderAmendmentChangeSnapshot(
    Guid OrderItemId,
    OrderAmendmentChangeKind Kind,
    int StartOrdinal,
    int Quantity,
    bool WholeLine,
    OrderItemDto Previous,
    OrderItemDto? Current,
    Guid? ReplacementDispatchedOrderId = null,
    string? ReplacementDispatchedOrderNumber = null);
