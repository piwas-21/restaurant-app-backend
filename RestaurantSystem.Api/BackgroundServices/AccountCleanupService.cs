using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.BackgroundServices;

public class AccountCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AccountCleanupService> _logger;

    // Tombstone for NON-nullable contact columns on retained business records
    // (nullable columns are cleared to null instead).
    private const string Erased = "[erased]";

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

                // Orders/reservations are retained as business/financial records
                // (bookkeeping) but the PERSON is erased from them (GDPR Art. 17):
                // clear the FK AND scrub the denormalized contact snapshots.
                // Previously only the FK was nulled, so name/email/phone/address
                // survived — an incomplete erasure. Capture the ids first (before
                // the FK is cleared) so the delivery-address snapshots can be scrubbed.
                // soft-delete-bypass: erasure is a permanent purge, NOT a restore —
                // it must reach soft-deleted orders to remove their PII too.
                var orderIds = await context.Orders
                    .IgnoreQueryFilters()
                    .Where(o => o.UserId == userId)
                    .Select(o => o.Id)
                    .ToListAsync(stoppingToken);

                // soft-delete-bypass: same GDPR-erasure rationale as the id capture above.
                await context.Orders
                    .IgnoreQueryFilters()
                    .Where(o => o.UserId == userId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.UserId, (Guid?)null)
                        .SetProperty(o => o.CustomerName, (string?)null)
                        .SetProperty(o => o.CustomerEmail, (string?)null)
                        .SetProperty(o => o.CustomerPhone, (string?)null)
                        // Free-text fields can embed identifying details too.
                        .SetProperty(o => o.Notes, (string?)null)
                        .SetProperty(o => o.CancellationReason, (string?)null)
                        // Only the free text goes: the order stays focused, and Focus.FocusedBy is
                        // the staff member who focused it, not the customer being erased.
                        .SetProperty(o => o.Focus!.Reason, (string?)null), stoppingToken);

                if (orderIds.Count > 0)
                {
                    await context.OrderAddresses
                        .Where(a => orderIds.Contains(a.OrderId))
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(a => a.Label, Erased)
                            .SetProperty(a => a.AddressLine1, Erased)
                            .SetProperty(a => a.AddressLine2, (string?)null)
                            .SetProperty(a => a.City, Erased)
                            .SetProperty(a => a.State, (string?)null)
                            .SetProperty(a => a.PostalCode, Erased)
                            .SetProperty(a => a.Country, Erased)
                            .SetProperty(a => a.Phone, (string?)null)
                            .SetProperty(a => a.Latitude, (double?)null)
                            .SetProperty(a => a.Longitude, (double?)null)
                            .SetProperty(a => a.DeliveryInstructions, (string?)null), stoppingToken);
                }

                // Reservation's Name/Email/Phone columns are NOT NULL in the DB
                // (ReservationConfiguration .IsRequired() + migration nullable:false),
                // even though the entity types read string?/string — so tombstone
                // them (nulling would throw a NOT NULL violation here). SpecialRequests
                // and Notes are nullable free-text → cleared to null.
                await context.Reservations
                    .Where(r => r.CustomerId == userId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.CustomerId, (Guid?)null)
                        .SetProperty(r => r.CustomerName, Erased)
                        .SetProperty(r => r.CustomerEmail, Erased)
                        .SetProperty(r => r.CustomerPhone, Erased)
                        .SetProperty(r => r.SpecialRequests, (string?)null)
                        .SetProperty(r => r.Notes, (string?)null), stoppingToken);

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
