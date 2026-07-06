namespace RestaurantSystem.Api.Common.Templates;

/// <summary>
/// Restaurant identity injected into every email template (issue #115),
/// sourced from the <c>RestaurantInfo</c> singleton so a tenant's own
/// branding, not a hardcoded name, flows into all outgoing mail.
/// </summary>
public record EmailBranding(string Name, string City, string Email);
