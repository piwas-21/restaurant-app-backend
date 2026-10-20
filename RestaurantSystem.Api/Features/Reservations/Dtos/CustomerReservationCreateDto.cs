using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

/// <summary>Restaurant calendar day and advisory slot, without staff/table/status fields.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerReservationCreateDto(int? SchemaVersion, Guid OperationId, Guid TenantId,
    string TimeZone, DateOnly? Date, TimeSpan? StartTime, TimeSpan? EndTime, int? NumberOfGuests,
    string CustomerName, string CustomerEmail, string? CustomerPhone, string? SpecialRequests, string? PayloadHash = null, string? OwnerProof = null);
