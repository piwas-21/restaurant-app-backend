using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Npgsql;

namespace RestaurantSystem.Channels.Api;

public sealed class SandboxConsoleMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOptions<SandboxConsoleSettings> options)
    {
        if (!context.Request.Path.StartsWithSegments("/console") && !context.Request.Path.StartsWithSegments("/api/sandbox"))
        {
            await next(context);
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
        if (!options.Value.Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        try { await next(context); }
        catch (ChannelConsoleException ex) { await Error(context, ex.Status, ex.Message); }
        catch (NpgsqlException) { await Error(context, 503, "Sandbox storage is unavailable. Refresh status before retrying."); }
        catch (CryptographicException) { await Error(context, 503, "Sandbox token protection failed. Contact the administrator."); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    }

    private static async Task Error(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new { message }, context.RequestAborted);
    }
}
