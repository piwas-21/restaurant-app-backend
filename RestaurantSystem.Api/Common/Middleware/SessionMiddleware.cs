namespace RestaurantSystem.Api.Common.Middleware;

public class SessionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionMiddleware> _logger;

    public SessionMiddleware(RequestDelegate next, ILogger<SessionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sessionId = context.Request.Headers["X-Session-Id"].FirstOrDefault();

        if (!string.IsNullOrEmpty(sessionId) && !Guid.TryParse(sessionId, out _))
        {
            _logger.LogWarning("Rejected malformed X-Session-Id: {SessionId} from {IP}",
                sessionId, context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid X-Session-Id format. Must be a valid UUID." });
            return;
        }

        await _next(context);
    }
}
