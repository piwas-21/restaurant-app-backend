using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Common.Models;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Infrastructure.Persistence;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RestaurantSystem.Api.Common.Authentication;

/// <summary>
/// Validates an opaque <c>Authorization: Bearer sk_live_...</c> credential against the
/// <c>ApiTokens</c> table (docs/plans/API-TOKENS-PLAN.md §3).
/// </summary>
/// <remarks>
/// A database lookup per request, not a self-validating JWT, because INSTANT revocation is the
/// point of the feature: a signed token is valid until it expires by construction, which is
/// exactly the property that makes a leaked agent credential unfixable.
/// <para>
/// Expiry and revocation are checked HERE rather than per endpoint, so they hold for every
/// route without anything to remember.
/// </para>
/// </remarks>
public sealed class ApiTokenAuthenticationHandler
    : AuthenticationHandler<ApiTokenAuthenticationOptions>
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<ApiTokenAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var raw = ReadBearerValue();

        // NoResult, not Fail: a value that is not one of our tokens is not this scheme's
        // business, and failing here would turn a plain missing-credential request into a
        // scheme-specific error.
        if (!ApiTokenDefaults.LooksLikeApiToken(raw))
        {
            return AuthenticateResult.NoResult();
        }

        var db = Context.RequestServices.GetRequiredService<ApplicationDbContext>();
        var hash = ApiTokenHasher.ComputeHash(raw!);

        var token = await db.ApiTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, Context.RequestAborted);

        if (token is null)
        {
            return AuthenticateResult.Fail("Unknown API token");
        }

        var now = DateTime.UtcNow;

        if (token.RevokedAt is not null)
        {
            return AuthenticateResult.Fail("API token has been revoked");
        }

        if (token.ExpiresAt <= now)
        {
            return AuthenticateResult.Fail("API token has expired");
        }

        await TouchLastUsedAsync(db, token, now);

        return AuthenticateResult.Success(BuildTicket(token));
    }

    /// <summary>
    /// Stamps <c>LastUsedAt</c> at most once per minute. Every request would be an UPDATE on the
    /// row every request already reads, for an answer nobody needs to the second.
    /// </summary>
    private async Task TouchLastUsedAsync(
        ApplicationDbContext db, Domain.Entities.ApiToken token, DateTime now)
    {
        if (token.LastUsedAt is not null &&
            now - token.LastUsedAt.Value < ApiTokenDefaults.LastUsedWriteInterval)
        {
            return;
        }

        token.LastUsedAt = now;
        await db.SaveChangesAsync(Context.RequestAborted);
    }

    private AuthenticationTicket BuildTicket(Domain.Entities.ApiToken token)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, token.Id.ToString()),
            new(ClaimTypes.Name, token.Name),
            // The Admin role claim is what lets a token satisfy the existing [RequireAdmin]
            // attributes without touching a single endpoint. It is safe ONLY because
            // ApiTokenScopeFilter denies a token every endpoint that is not explicitly
            // annotated with the scope it holds — see API-TOKENS-PLAN §5.
            new(ClaimTypes.Role, UserRole.Admin.ToString()),
            new(ApiTokenDefaults.AuthMethodClaimType, ApiTokenDefaults.ApiTokenAuthMethod)
        };

        claims.AddRange(token.Scopes.Select(s => new Claim(ApiTokenDefaults.ScopeClaimType, s)));

        var identity = new ClaimsIdentity(claims, ApiTokenDefaults.AuthenticationScheme);
        return new AuthenticationTicket(
            new ClaimsPrincipal(identity), ApiTokenDefaults.AuthenticationScheme);
    }

    private string? ReadBearerValue()
    {
        var header = Context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        WriteEnvelopeAsync(
            StatusCodes.Status401Unauthorized,
            ApiResponse<object>.Failure(
                "Authentication required",
                "A valid API token or login is required to access this resource"));

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteEnvelopeAsync(
            StatusCodes.Status403Forbidden,
            ApiResponse<object>.Failure(
                "Access denied", "This API token may not access this resource"));

    private async Task WriteEnvelopeAsync(int statusCode, ApiResponse<object> body)
    {
        Response.StatusCode = statusCode;
        Response.ContentType = "application/json";
        await Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
