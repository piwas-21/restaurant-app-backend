using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerReservationCancellationDto(int? SchemaVersion,
    [property: JsonRequired] Guid OperationId, [property: JsonRequired] Guid TenantId,
    [property: JsonRequired] Guid ReservationId, string TimeZone);
