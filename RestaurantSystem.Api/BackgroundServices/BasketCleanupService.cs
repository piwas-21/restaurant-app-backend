
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.BackgroundServices;

public class BasketCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BasketCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(6); // Run every 6 hours

    public BasketCleanupService(IServiceProvider serviceProvider, ILogger<BasketCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredBasketsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during basket cleanup");
            }

            await Task.Delay(_cleanupInterval, stoppingToken);
        }
    }

    private async Task CleanupExpiredBasketsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cutoffDate = DateTime.UtcNow;

        // Find expired baskets
        var expiredBaskets = await context.Baskets
            .Where(b => !b.IsDeleted && b.ExpiresAt.HasValue && b.ExpiresAt < cutoffDate)
            .ToListAsync(cancellationToken);

        if (expiredBaskets.Any())
        {
            foreach (var basket in expiredBaskets)
            {
                basket.IsDeleted = true;
                basket.DeletedAt = DateTime.UtcNow;
                basket.DeletedBy = "BasketCleanupService";
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Cleaned up {Count} expired baskets", expiredBaskets.Count);
        }
    }
}
