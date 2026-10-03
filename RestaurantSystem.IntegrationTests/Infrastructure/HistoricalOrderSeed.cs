using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>Seeds the historical order columns without today's EF INSERT/RETURNING shape.</summary>
internal static class HistoricalOrderSeed
{
    internal static Task InsertAsync(ApplicationDbContext context, Guid id, string number,
        DateTime instant, string actor, bool deleted = false)
    {
        DateTime? deletedAt = deleted ? instant : null;
        string? deletedBy = deleted ? actor : null;
        return context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO orders
              (id, order_number, type, sub_total, tax, delivery_fee, discount, discount_percentage,
               tip, total, has_user_limit_discount, user_limit_amount, status, payment_status,
               order_date, created_at, created_by, is_deleted, deleted_at, deleted_by)
            VALUES
              ({id}, {number}, 'Takeaway', 0, 0, 0, 0, 0, 0, 0, false, 0, 'Pending', 'Pending',
               {instant}, {instant}, {actor}, {deleted}, {deletedAt}, {deletedBy});
            """);
    }
}
