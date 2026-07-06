using RestaurantSystem.Api.Common.Templates;

namespace RestaurantSystem.Api.Common.Services;

/// <summary>
/// Resolves the <see cref="EmailBranding"/> used by every email template from the
/// <c>RestaurantInfo</c> singleton (issue #115), so branding is admin-editable
/// instead of hardcoded in the templates.
/// </summary>
public interface IEmailBrandingProvider
{
    /// <summary>
    /// Gets the current restaurant's email branding. Cached for the lifetime of the
    /// owning scope, so callers can invoke this once per request/handler.
    /// </summary>
    Task<EmailBranding> GetAsync(CancellationToken ct = default);
}
