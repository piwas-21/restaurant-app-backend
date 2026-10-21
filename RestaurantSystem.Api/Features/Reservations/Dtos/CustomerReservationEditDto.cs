using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerReservationEditDto(int? SchemaVersion,
    [property: JsonRequired] Guid OperationId, [property: JsonRequired] Guid TenantId,
    [property: JsonRequired] Guid ReservationId, string TimeZone, DateOnly? Date,
    TimeSpan? StartTime, TimeSpan? EndTime, int? NumberOfGuests,
    string CustomerName, string CustomerEmail, string? CustomerPhone, string? SpecialRequests,
    string? PayloadHash = null, string? OwnerProof = null);

public sealed record CustomerReservationEditPreparedDto(int SchemaVersion, CustomerReservationProofDto Proof,
    CustomerReservationItemDto Original, CustomerReservationItemDto Proposed, string TimeZone);
