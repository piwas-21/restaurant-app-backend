using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Common
{
    /// <summary>
    /// Maps build-identity / diagnostics endpoints so "what is deployed right now?"
    /// is answerable with a single GET, without SSHing into the box.
    /// </summary>
    public static class VersionEndpoints
    {
        public static void MapVersionEndpoints(this WebApplication app)
        {
            // Public, minimal build identity. The repos are private, so a short commit
            // SHA leaks little; this is the industry-norm "build info" surface. The
            // frontend aggregates this into its own /api/version so one URL shows both.
            app.MapGet("/api/version", () => Results.Ok(new
            {
                service = "restaurant-system-api",
                commit = BuildInfo.ShortCommitSha,
                buildTime = BuildInfo.BuildTime,
                environment = app.Environment.EnvironmentName,
                uptimeSeconds = (long)BuildInfo.Uptime.TotalSeconds,
            }))
            .WithName("Version")
            .AllowAnonymous();

            // Admin-only, richer diagnostics. DELIBERATELY excludes application logs,
            // environment-variable dumps and connection strings: those carry PII /
            // secrets and must never be exposed over HTTP (see the deploy runbook —
            // logs are read via `docker compose logs` / Dozzle behind auth instead).
            app.MapGet("/api/diagnostics", async (ApplicationDbContext db, CancellationToken ct) =>
            {
                var canConnect = await db.Database.CanConnectAsync(ct);
                var lastMigration = canConnect
                    ? (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault()
                    : null;
                var pendingMigrations = canConnect
                    ? (await db.Database.GetPendingMigrationsAsync(ct)).Count()
                    : -1;

                return Results.Ok(new
                {
                    service = "restaurant-system-api",
                    commit = BuildInfo.CommitSha,
                    buildTime = BuildInfo.BuildTime,
                    environment = app.Environment.EnvironmentName,
                    framework = RuntimeInformation.FrameworkDescription,
                    host = Environment.MachineName,
                    serverTimeUtc = DateTime.UtcNow,
                    startedUtc = BuildInfo.ProcessStartUtc,
                    uptimeSeconds = (long)BuildInfo.Uptime.TotalSeconds,
                    database = new
                    {
                        canConnect,
                        lastAppliedMigration = lastMigration,
                        pendingMigrations,
                    },
                });
            })
            .WithName("Diagnostics")
            .RequireAuthorization(new AuthorizeAttribute { Roles = "Admin" });
        }
    }
}
