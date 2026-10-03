using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.OrderAmendments.Services;

internal static class OrderAmendmentResolutionClientKeyLock
{
    internal static async Task AcquireAsync(ApplicationDbContext context, Guid actorId,
        Guid clientOperationId, CancellationToken cancellationToken)
    {
        var lockName = Encoding.UTF8.GetBytes($"{actorId:D}:{clientOperationId:D}");
        var lockKey = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(lockName));
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})", cancellationToken);
    }
}
