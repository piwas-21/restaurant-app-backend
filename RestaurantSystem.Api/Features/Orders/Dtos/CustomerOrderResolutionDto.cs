using RestaurantSystem.Api.Features.Orders.Services;

namespace RestaurantSystem.Api.Features.Orders.Dtos;

/// <summary>Opaque acceptance proof survives withdrawal of the order's private detail.</summary>
public sealed record CustomerOrderResolutionDto(int SchemaVersion, Guid OperationId,
    CustomerOrderOperationOutcome Outcome, Guid? OrderId);
