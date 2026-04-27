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

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
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

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
