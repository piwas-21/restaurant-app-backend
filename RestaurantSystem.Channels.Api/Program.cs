using RestaurantSystem.Channels.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddChannelGateway(builder.Configuration);
var app = builder.Build();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "sofra-channel-gateway",
    version = Environment.GetEnvironmentVariable("GIT_SHA") ?? "unknown",
    builtAt = Environment.GetEnvironmentVariable("BUILD_TIME") ?? "unknown",
}));
await app.RunAsync();

public partial class Program
{
    protected Program() { }
}
