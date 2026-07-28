using Microsoft.AspNetCore.Authentication;
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
    /// <see cref="RoleHeader"/>. Not seeded in the users table — nothing that authenticates
    /// through this handler requires the caller's own row to exist.
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

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Opt-in "no credentials at all", for endpoints whose whole point is that a guest
        // with no token can reach them.
        if (Context.Request.Headers.ContainsKey(AnonymousHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
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

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
