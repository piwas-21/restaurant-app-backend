using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.KitchenBoard.Services;

internal static class KitchenBoardSequenceLock
{
    private const int AdvisoryLockNamespace = 664311;
    private const int AdvisoryLockId = 2;

    internal static Task<int> AcquireAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken) => context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AdvisoryLockNamespace}, {AdvisoryLockId})",
            cancellationToken);
}
