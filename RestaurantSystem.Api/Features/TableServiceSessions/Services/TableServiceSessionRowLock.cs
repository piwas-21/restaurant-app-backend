using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.TableServiceSessions.Services;

/// <summary>Loads one service-session row with a PostgreSQL row lock for close/create serialization.</summary>
public static class TableServiceSessionRowLock
{
    public static Task<Table?> LoadTableAsync(
        ApplicationDbContext context, Guid tableId, CancellationToken cancellationToken) =>
        context.Tables
            .FromSqlInterpolated($"SELECT * FROM \"Tables\" WHERE id = {tableId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public static Task<TableServiceSession?> LoadAsync(
        ApplicationDbContext context, Guid serviceSessionId, CancellationToken cancellationToken) =>
        context.TableServiceSessions
            .FromSqlInterpolated($"SELECT * FROM table_service_sessions WHERE id = {serviceSessionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
}
