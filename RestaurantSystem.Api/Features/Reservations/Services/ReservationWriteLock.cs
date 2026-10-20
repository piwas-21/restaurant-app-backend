using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Reservations.Services;

/// <summary>Serializes booked-slot writers across this tenant database, before their first read.
/// A coarse schedule lock avoids stale source days and opposite-day move deadlocks. Live dine-in
/// holds are a separate system. Read-only availability stays advisory and does not take this lock.
/// The caller commits its owned transaction before sending mail; early returns roll it back.</summary>
public static class ReservationWriteLock
{
    private static readonly long ScheduleKey = BinaryPrimitives.ReadInt64LittleEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes("sofra:reservation-schedule:v1")));

    public static async Task<IDbContextTransaction> BeginAsync(
        ApplicationDbContext context, CancellationToken cancellationToken)
    {
        var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({ScheduleKey})", cancellationToken);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
