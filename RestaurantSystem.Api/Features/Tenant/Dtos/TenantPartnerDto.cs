namespace RestaurantSystem.Api.Features.Tenant.Dtos;

/// <summary>Public attribution; email is supplied only for the platform default.</summary>
public record TenantPartnerDto(string? Name, string? Url, string? Email = null);
