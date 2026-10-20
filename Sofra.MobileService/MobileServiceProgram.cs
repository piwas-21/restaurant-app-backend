using Sofra.MobileService.Directory;
using Sofra.MobileService.Links;

namespace Sofra.MobileService;

public sealed class MobileServiceProgram
{
    private MobileServiceProgram() { }

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddMobileDirectory(builder.Configuration);
        builder.Services.AddMobileLinks(builder.Configuration);
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.UseExceptionHandler();
        app.MapMobileDirectory();
        app.MapMobileLinks();
        // Intentionally anonymous liveness for infrastructure probes. Fixed status only;
        // no configuration, tenant identities, dependency details or customer data are exposed.
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.Run();
    }
}
