using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.TableGuestVisits.Dtos;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerTableRoundProofDto(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] Guid OperationId,
    [property: JsonRequired] Guid TenantId,
    [property: JsonRequired] string PayloadHash,
    [property: JsonRequired] string OwnerProof);
