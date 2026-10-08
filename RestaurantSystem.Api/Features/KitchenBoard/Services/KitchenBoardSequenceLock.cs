using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal static class KitchenBoardSequenceLock
{
    // These IDs are part of the database sequence protocol. The migration-installed
    // next_kitchen_board_change_sequence and next_kitchen_board_completion_sequence
    // functions acquire the same transaction lock; changing or configuring this pair
    // independently would break ordering between trigger and application writers.
    private const int AdvisoryLockNamespace = 664311;
    private const int AdvisoryLockId = 2;

    internal static Task<int> AcquireAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockNamespace}, {AdvisoryLockId})",
            cancellationToken);
}
