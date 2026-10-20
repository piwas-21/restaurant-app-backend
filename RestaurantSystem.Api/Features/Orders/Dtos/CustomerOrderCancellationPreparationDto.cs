using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record CustomerOrderCancellationPreparationDto(
    int SchemaVersion, Guid OrderId, int ExpectedVersion, bool Allowed,
    CustomerOrderCancellationReason? Reason, string? PayloadHash);
