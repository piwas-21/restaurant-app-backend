using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.BackgroundServices;

/// <summary>Erases expired private challenges even after native social sign-in is disabled.</summary>
public sealed class NativeSocialChallengeCleanupService(IServiceScopeFactory scopes, TimeProvider time,
    ILogger<NativeSocialChallengeCleanupService> logger) : BackgroundService
{
    private const int BatchSize = 1000;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await CleanupAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Native social challenge cleanup failed."); }
        }
    }
    internal async Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = time.GetUtcNow().UtcDateTime;
        // Row-only deletion never waits on User after holding a challenge lock.
        return await context.CustomerSocialChallenges.Where(row => row.ExpiresAt <= now)
            .OrderBy(row => row.ExpiresAt).ThenBy(row => row.Id).Take(BatchSize)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
