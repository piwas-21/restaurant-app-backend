namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public enum CustomerReservationOperationOutcome { Applied, Cancelled, Unseen }

/// <summary>Minimal protected-action decision; not private booking detail or restaurant confirmation.
/// Cancelled means the original attempt was stopped before its effect was applied.</summary>
public sealed record CustomerReservationResolutionDto(int SchemaVersion, Guid OperationId,
    CustomerReservationOperationOutcome Outcome, Guid? ReservationId);
