using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.AccountPayments.Services;

/// <summary>Serializes one idempotency key across the separate plan and attempt tables.</summary>
internal static class AccountPaymentOperationKeyLock
{
    internal static Task AcquireAsync(
        ApplicationDbContext context, Guid operationId, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({operationId.ToString("N")}, 0))",
            cancellationToken);
}
