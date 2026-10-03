using Microsoft.Extensions.Options;
using RestaurantSystem.Api.Features.AccountPayments.Services;
using RestaurantSystem.Api.Settings;

namespace RestaurantSystem.Api.BackgroundServices;

/// <summary>Tenant-scoped recovery continues for existing contributions when new guest payments are disabled.</summary>
public sealed class AccountCheckoutReconciliationService(IServiceScopeFactory scopes,
    IOptions<AccountCheckoutSettings> options, ILogger<AccountCheckoutReconciliationService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.ReconciliationIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await SweepAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    AccountCheckoutDiagnostics.Warn(logger, exception, AccountCheckoutFailurePhase.Sweep);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown preserves leases and reservations for the next process to recover.
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> due;
        await using (var scope = scopes.CreateAsyncScope())
            due = await scope.ServiceProvider.GetRequiredService<IAccountCheckoutLeaseStore>().ReadDueAsync(cancellationToken);
        foreach (var attemptId in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<IAccountCheckoutReconciler>()
                    .ReconcileAsync(attemptId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                AccountCheckoutDiagnostics.Warn(logger, exception, AccountCheckoutFailurePhase.Sweep, attemptId);
            }
        }
    }
}
