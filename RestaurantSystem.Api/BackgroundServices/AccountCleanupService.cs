using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.BackgroundServices;

public class AccountCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AccountCleanupService> _logger;

    public AccountCleanupService(
        IServiceProvider serviceProvider,
        ILogger<AccountCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AccountCleanupService is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDeletionRequests(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing account deletions.");
            }

            // Run every 24 hours
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    internal async Task ProcessDeletionRequests(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;

        var usersToDelete = await context.Users
            .IgnoreQueryFilters()
            .Where(u => u.DeletionScheduledAt != null && u.DeletionScheduledAt < now)
            .ToListAsync(stoppingToken);

        if (!usersToDelete.Any())
            return;

        _logger.LogInformation("Found {Count} users scheduled for deletion.", usersToDelete.Count);

        foreach (var user in usersToDelete)
        {
            using var transaction = await context.Database.BeginTransactionAsync(stoppingToken);
            try
            {
                var userId = user.Id;

                var scrubber = scope.ServiceProvider.GetRequiredService<IRetainedCustomerDataScrubber>();
                await scrubber.ScrubAsync(userId, stoppingToken);

                // Delete user-owned data (personal data that should be purged)
                await context.Baskets.IgnoreQueryFilters().Where(b => b.UserId == userId).ExecuteDeleteAsync(stoppingToken);
                await context.UserAddresses.IgnoreQueryFilters().Where(a => a.UserId == userId).ExecuteDeleteAsync(stoppingToken);
                await context.FidelityPointBalances.IgnoreQueryFilters().Where(f => f.UserId == userId).ExecuteDeleteAsync(stoppingToken);
                await context.CustomerDiscountRules.IgnoreQueryFilters().Where(r => r.UserId == userId).ExecuteDeleteAsync(stoppingToken);

                // Hard delete the user row
                await context.Users.IgnoreQueryFilters().Where(u => u.Id == userId).ExecuteDeleteAsync(stoppingToken);

                await transaction.CommitAsync(stoppingToken);

                _logger.LogInformation("Permanently deleted user {UserId} scheduled for {Date}", userId, user.DeletionScheduledAt);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(stoppingToken);
                _logger.LogError(ex, "Exception deleting user {UserId}", user.Id);
            }
        }
    }
}
