using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record CustomerOrderHistoryCursorDto(DateTime OrderDate, Guid OrderId);
public sealed record CustomerOrderHistoryItemDto(Guid OrderId, string OrderNumber, OrderType Type,
    DateTime OrderDate, OrderStatus Status, PaymentStatus PaymentStatus, string? Currency, decimal? PayableTotal);
public sealed record CustomerOrderHistoryPageDto(int SchemaVersion, string BusinessTimeZoneId,
    IReadOnlyList<CustomerOrderHistoryItemDto> Items, CustomerOrderHistoryCursorDto? NextBefore);
