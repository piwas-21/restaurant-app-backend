using RestaurantSystem.Domain.Common.Enums;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

public sealed record CustomerOrderCancellationResolutionDto(
    int SchemaVersion, Guid OperationId, Guid OrderId, CustomerOrderCancellationOutcome Outcome,
    int? ObservedVersion, CustomerOrderCancellationReason? Reason);
