using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Reservations.Dtos;

/// <summary>PII-free original-attempt descriptor, always used with its original session/principal.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerReservationProofDto(int? SchemaVersion, Guid OperationId, Guid TenantId, string PayloadHash, string OwnerProof);
