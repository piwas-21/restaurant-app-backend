using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace RestaurantSystem.Channels.Api;

[ApiController]
[Route("api/sandbox/auth")]
[EnableRateLimiting("sandbox-login")]
[RequestSizeLimit(4096)]
public sealed class SandboxAuthController(ISandboxSessions sessions, IOptions<SandboxConsoleSettings> options) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(ConsoleLoginRequest request, CancellationToken cancellationToken)
    {
        sessions.RequireOrigin(HttpContext);
        var session = await sessions.Login(request.AccessKey, cancellationToken);
        Response.Cookies.Append(SandboxConsoleSettings.CookieName, session, CookieOptions());
        return Ok(new { authenticated = true });
    }

    [HttpGet("session")]
    public async Task<IActionResult> Session(CancellationToken cancellationToken)
    {
        await sessions.Require(HttpContext, false, cancellationToken);
        return Ok(new { authenticated = true });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var session = await sessions.Require(HttpContext, true, cancellationToken);
        await sessions.Logout(session, cancellationToken);
        Response.Cookies.Delete(SandboxConsoleSettings.CookieName, CookieOptions());
        return NoContent();
    }

    private CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = TimeSpan.FromMinutes(options.Value.SessionMinutes),
        IsEssential = true,
    };
}
