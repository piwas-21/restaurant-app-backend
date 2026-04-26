using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
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
        using (var scope = _serviceProvider.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var now = DateTime.UtcNow;

            var usersToDelete = await context.Users
                .Where(u => u.DeletionScheduledAt != null && u.DeletionScheduledAt < now)
                .ToListAsync(stoppingToken);

            if (usersToDelete.Any())
            {
                _logger.LogInformation("Found {Count} users scheduled for deletion.", usersToDelete.Count);

                foreach (var user in usersToDelete)
                {
                    try
                    {

                        var result = await userManager.DeleteAsync(user);
                        if (result.Succeeded)
                        {
                            _logger.LogInformation("Permanently deleted user {UserId} (Scheduled: {Date})", user.Id, user.DeletionScheduledAt);
                        }
                        else
                        {
                            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                            _logger.LogError("Failed to delete user {UserId}: {Errors}", user.Id, errors);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception deleting user {UserId}", user.Id);
                    }
                }
            }
        }
    }
}
