using Microsoft.AspNetCore.Authentication;
using RestaurantSystem.Api.Common.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace RestaurantSystem.IntegrationTests.Common;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string UserId = "cd6c41d9-97e1-4fb4-9bee-ab6a9b460471";
    public const string UserName = "test@example.com";
    public const string AdminUserId = "11111111-1111-1111-1111-111111111111";
    public const string AdminUserName = "admin@example.com";

    /// <summary>
    /// Non-admin back-of-house identity (Cashier / KitchenStaff / Server), selected with
    /// <see cref="RoleHeader"/>. Not seeded by <c>TestDataSeeder</c>.
    /// <para>
    /// AUTHENTICATION does not need the row, but PERSISTENCE can: an endpoint that writes the
    /// caller's id into a column with an <c>AspNetUsers</c> foreign key answers 500 without it.
    /// <c>POST /api/orders</c> is one (<c>fk_orders_asp_net_users_user_id</c>, measured 2026-08-28
    /// by <c>WaiterLineIngredientSelectionTests</c>, which seeds the row itself). A test class that
    /// uses <see cref="RoleHeader"/> AND creates a row owned by the caller must seed this user.
    /// It is not in the shared seeder deliberately — that seeder feeds every test class, and a
    /// third user row would silently move any assertion that counts or enumerates users.
    /// </para>
    /// </summary>
    public const string StaffUserId = "33333333-3333-3333-3333-333333333333";
    public const string StaffUserName = "staff@example.com";

    /// <summary>Sets the caller's role, e.g. "Cashier". Ignored when <see cref="AnonymousHeader"/> is present.</summary>
    public const string RoleHeader = "X-Test-Role";

    /// <summary>
    /// Produces a genuinely unauthenticated request. Without it this handler always succeeds,
    /// so clearing the Authorization header does NOT simulate a guest — every request still
    /// arrives as the default Customer.
    /// </summary>
    public const string AnonymousHeader = "X-Test-Anonymous";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // A machine API token is authenticated by the REAL handler, not faked here. This host
        // replaces the default scheme wholesale, so without this forward the ApiToken scheme
        // would never run and every token test would silently assert against a fake admin.
        // It mirrors what Program.cs's BearerSelector policy scheme does in production.
        if (ApiTokenDefaults.LooksLikeApiToken(ReadBearerValue()))
        {
            return await Context.AuthenticateAsync(ApiTokenDefaults.AuthenticationScheme);
        }

        // Opt-in "no credentials at all", for endpoints whose whole point is that a guest
        // with no token can reach them.
        if (Context.Request.Headers.ContainsKey(AnonymousHeader))
        {
            return AuthenticateResult.NoResult();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, UserId),
            new(ClaimTypes.Name, UserName),
            new(ClaimTypes.Email, UserName),
            new(ClaimTypes.Role, "Customer")
        };

        // Check if admin header is present
        if (Context.Request.Headers.TryGetValue("X-Test-Admin", out var isAdmin) && isAdmin == "true")
        {
            claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, AdminUserId),
                new(ClaimTypes.Name, AdminUserName),
                new(ClaimTypes.Email, AdminUserName),
                new(ClaimTypes.Role, "Admin")
            };
        }
        else if (Context.Request.Headers.TryGetValue(RoleHeader, out var role)
                 && !string.IsNullOrWhiteSpace(role))
        {
            claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, StaffUserId),
                new(ClaimTypes.Name, StaffUserName),
                new(ClaimTypes.Email, StaffUserName),
                new(ClaimTypes.Role, role.ToString())
            };
        }

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return AuthenticateResult.Success(ticket);
    }

    private string? ReadBearerValue()
    {
        var header = Context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }
}
