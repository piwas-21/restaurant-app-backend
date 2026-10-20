using System.Text.Json.Serialization;

namespace RestaurantSystem.Api.Features.Auth.Dtos;

/// <summary>Encrypted/authenticated payload. Never returned as a DTO or written to logs.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerNativeAccountProof(int SchemaVersion, Guid TenantId, Guid UserId,
    string Email, string Token, long IssuedAtTicks, long ExpiresAtTicks);
