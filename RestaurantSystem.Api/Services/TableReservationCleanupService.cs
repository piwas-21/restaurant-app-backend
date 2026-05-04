using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Services;

/// <summary>
/// Background service that automatically releases expired table reservations
/// Runs every 5 minutes to clean up reservations past their ReservedUntil time
/// </summary>
public class TableReservationCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<TableReservationCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public TableReservationCleanupService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<TableReservationCleanupService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Table Reservation Cleanup Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReleaseExpiredReservations(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while releasing expired table reservations");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("Table Reservation Cleanup Service stopped");
    }

    private async Task ReleaseExpiredReservations(CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var now = DateTime.UtcNow;

        var expiredReservations = await context.TableReservations
            .Where(r => r.IsActive && r.ReservedUntil <= now)
            .ToListAsync(cancellationToken);

        if (expiredReservations.Any())
        {
            foreach (var reservation in expiredReservations)
            {
                reservation.IsActive = false;
                reservation.ReleasedAt = now;
                reservation.ReleaseReason = "Expired";

                _logger.LogInformation(
                    "Auto-released expired reservation for table {TableNumber} (was reserved until {ReservedUntil})",
                    reservation.TableNumber,
                    reservation.ReservedUntil);
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Released {Count} expired table reservations",
                expiredReservations.Count);
        }
    }
}
