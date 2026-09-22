namespace RestaurantSystem.Api.Features.Tenant.Dtos;

/// <summary>Tenant rollout switches needed by the server-rendered frontend shell.</summary>
public sealed record TenantFeaturesDto(bool ServerWorkspaceV2);
