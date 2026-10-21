using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

/// <summary>PII-free original-attempt descriptor, always used with its original session/principal.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerReservationProofDto(int? SchemaVersion, [property: JsonRequired] Guid OperationId, [property: JsonRequired] Guid TenantId, string PayloadHash, string OwnerProof);
