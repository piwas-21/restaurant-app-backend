using Microsoft.EntityFrameworkCore;
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

    private async Task ProcessDeletionRequests(CancellationToken stoppingToken)
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

                // Unlink orders and reservations (business records — keep rows, clear FK)
                await context.Orders
                    .Where(o => o.UserId == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, (Guid?)null), stoppingToken);

                await context.Reservations
                    .Where(r => r.CustomerId == userId)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.CustomerId, (Guid?)null), stoppingToken);

                // Unlink discount rule references on orders before deleting discount rules
                var discountRuleIds = await context.CustomerDiscountRules
                    .Where(r => r.UserId == userId)
                    .Select(r => r.Id)
                    .ToListAsync(stoppingToken);

                if (discountRuleIds.Any())
                {
                    await context.Orders
                        .Where(o => o.CustomerDiscountRuleId.HasValue && discountRuleIds.Contains(o.CustomerDiscountRuleId.Value))
                        .ExecuteUpdateAsync(s => s.SetProperty(o => o.CustomerDiscountRuleId, (Guid?)null), stoppingToken);
                }

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
