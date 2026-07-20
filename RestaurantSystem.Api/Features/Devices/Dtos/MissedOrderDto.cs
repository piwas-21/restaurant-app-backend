using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Devices.Dtos;

/// <summary>A confirmed order that is past the grace window with no <c>Printed</c> device receipt —
/// i.e. it should have printed by now but hasn't (the 2026-07-19 incident's signal). Non-PII only:
/// no customer name/address/phone — just what an operator needs to locate the ticket.</summary>
public record MissedOrderDto(
    Guid OrderId,
    string OrderNumber,
    OrderType Type,
    int? TableNumber,
    DateTime OrderDate
);
