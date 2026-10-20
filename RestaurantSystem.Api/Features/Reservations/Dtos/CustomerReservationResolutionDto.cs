namespace RestaurantSystem.Api.Features.Reservations.Dtos;

public enum CustomerReservationOperationOutcome { Applied, Cancelled, Unseen }

/// <summary>Minimal stable decision; not confirmation, private booking detail, or a cancellation of an applied booking.</summary>
public sealed record CustomerReservationResolutionDto(int SchemaVersion, Guid OperationId,
    CustomerReservationOperationOutcome Outcome, Guid? ReservationId);
