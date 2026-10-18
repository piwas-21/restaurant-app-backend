using Sofra.MobileService.Directory;

namespace Sofra.MobileService;

public sealed class MobileServiceProgram
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddMobileDirectory(builder.Configuration);
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapMobileDirectory();
        // Intentionally anonymous liveness for infrastructure probes. Fixed status only;
        // no configuration, tenant identities, dependency details or customer data are exposed.
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.Run();
    }
}
