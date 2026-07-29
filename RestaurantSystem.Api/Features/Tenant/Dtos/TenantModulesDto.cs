namespace RestaurantSystem.Api.Features.Tenant.Dtos;

/// <summary>
/// The tenant's effective module set, as served to the frontend (sofra ADR-010 / S11).
/// </summary>
/// <param name="Modules">
/// Effective allow-list in catalog order. When enforcement is off this is the whole
/// vocabulary, so a client can test membership without also checking
/// <paramref name="Enforced"/>.
/// </param>
/// <param name="Enforced">
/// Whether this instance is gating on the list at all. Diagnostic — it is what
/// distinguishes "everything, because unrestricted" from "everything, because bought".
/// </param>
public record TenantModulesDto(IReadOnlyList<string> Modules, bool Enforced);
