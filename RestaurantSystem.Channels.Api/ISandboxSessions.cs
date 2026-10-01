namespace RestaurantSystem.Channels.Api;

public interface ISandboxSessions
{
    Task<string> Login(string accessKey, CancellationToken cancellationToken);
    Task<string> Require(HttpContext context, bool mutating, CancellationToken cancellationToken);
    Task Logout(string sessionHash, CancellationToken cancellationToken);
    void RequireOrigin(HttpContext context);
}
