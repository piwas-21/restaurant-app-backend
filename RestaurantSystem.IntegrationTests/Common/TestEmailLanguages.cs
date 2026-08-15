using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Services;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Settings;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// A REAL <see cref="IEmailLanguageResolver"/> for the tests that build a handler by hand
/// (EMAIL-LOCALISATION-PLAN S5). Deliberately not a mock: a loose mock answers <c>null</c> for
/// <c>TenantDefault</c> and every send site would then render whatever
/// <c>CultureInfo.GetCultureInfo(null)</c> does, which is exactly the class of bug this slice
/// exists to prevent. The real thing with no configuration answers <c>en</c>, like every
/// unconfigured tenant.
/// </summary>
internal static class TestEmailLanguages
{
    /// <param name="supported">Comma-separated codes, or null for the product's ten.</param>
    /// <param name="defaultLanguage">The tenant default, or null for <c>en</c>.</param>
    public static IEmailLanguageResolver Resolver(string? supported = null, string? defaultLanguage = null) =>
        new EmailLanguageResolver(
            Options.Create(new LocalizationSettings
            {
                SupportedLanguages = supported ?? string.Empty,
                DefaultLanguage = defaultLanguage ?? string.Empty
            }),
            // No request, ever: nothing built through this helper is running on a guest's own
            // thread, and an accessor that could see one would let a test pass for the wrong reason.
            new NoRequestAccessor(),
            NullLogger<EmailLanguageResolver>.Instance);

    private sealed class NoRequestAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
