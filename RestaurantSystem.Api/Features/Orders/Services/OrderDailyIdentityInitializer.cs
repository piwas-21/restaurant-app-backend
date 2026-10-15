using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Services.Interfaces;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Orders.Services;

/// <summary>Completes legacy daily order identities before the HTTP server accepts traffic.</summary>
public sealed class OrderDailyIdentityInitializer(
    ApplicationDbContext context,
    ITenantClock clock,
    ILogger<OrderDailyIdentityInitializer> logger) : IOrderDailyIdentityInitializer
{
    private const int LockNamespace = 1;
    private const int StartupLockDay = int.MinValue;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var timeZoneId = clock.TimeZone.Id;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await IsInitializedAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({LockNamespace}, {StartupLockDay})", cancellationToken);
        if (await IsInitializedAsync(cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var todayKey = int.Parse(clock.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({LockNamespace}, {todayKey})", cancellationToken);
        await LockExistingDaysAsync(timeZoneId, cancellationToken);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE orders
            SET business_date = COALESCE(
                try_order_number_day(order_number),
                (order_date AT TIME ZONE {timeZoneId})::date)
            WHERE business_date IS NULL
            """, cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            DO $block$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM (
                        SELECT business_date, daily_order_number
                        FROM orders
                        WHERE daily_order_number IS NOT NULL
                        UNION ALL
                        SELECT try_order_number_day(order_number),
                               try_order_number_sequence(order_number)
                        FROM orders
                        WHERE daily_order_number IS NULL
                          AND try_order_number_day(order_number) IS NOT NULL
                          AND try_order_number_sequence(order_number) IS NOT NULL
                    ) identities
                    GROUP BY business_date, daily_order_number
                    HAVING count(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Duplicate parsed daily order numbers prevent safe historical backfill.';
                END IF;
            END;
            $block$;
            """, cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            WITH existing AS (
                SELECT business_date, max(daily_order_number) AS last_number
                FROM orders
                WHERE daily_order_number IS NOT NULL
                GROUP BY business_date
                UNION ALL
                SELECT try_order_number_day(order_number), max(try_order_number_sequence(order_number))
                FROM orders
                WHERE daily_order_number IS NULL
                  AND try_order_number_day(order_number) IS NOT NULL
                  AND try_order_number_sequence(order_number) IS NOT NULL
                GROUP BY try_order_number_day(order_number)
            ), maxima AS (
                SELECT business_date, max(last_number) AS last_number
                FROM existing
                GROUP BY business_date
            ), pending AS (
                SELECT id, business_date, try_order_number_sequence(order_number) AS parsed_number,
                    try_order_number_day(order_number) AS parsed_date
                FROM orders
                WHERE daily_order_number IS NULL
            ), ranked AS (
                SELECT pending.id, pending.parsed_number, pending.parsed_date,
                    COALESCE(existing.last_number, 0)
                      + row_number() OVER (
                          PARTITION BY pending.business_date
                          ORDER BY ordering.order_date, pending.id) AS assigned_number
                FROM pending
                LEFT JOIN maxima existing USING (business_date)
                JOIN orders ordering ON ordering.id = pending.id
            )
            UPDATE orders target
            SET daily_order_number = CASE
                WHEN ranked.parsed_date IS NOT NULL AND ranked.parsed_number IS NOT NULL
                    THEN ranked.parsed_number
                ELSE ranked.assigned_number
            END
            FROM ranked
            WHERE target.id = ranked.id;
            """, cancellationToken);

        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO order_number_sequences (day, last_sequence)
            SELECT business_date, max(daily_order_number)
            FROM orders
            GROUP BY business_date
            ON CONFLICT (day) DO UPDATE
            SET last_sequence = GREATEST(
                order_number_sequences.last_sequence, EXCLUDED.last_sequence);

            ALTER TABLE orders ALTER COLUMN business_date SET NOT NULL;
            ALTER TABLE orders ALTER COLUMN daily_order_number SET NOT NULL;
            """, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Daily order identities are initialized for timezone {BusinessTimeZoneId}", timeZoneId);
    }

    private async Task<bool> IsInitializedAsync(CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<bool>("""
            SELECT count(*) = 2 AND COALESCE(bool_and(is_nullable = 'NO'), false) AS "Value"
            FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND table_name = 'orders'
              AND column_name IN ('business_date', 'daily_order_number')
            """).SingleAsync(cancellationToken);

    private Task<int> LockExistingDaysAsync(string timeZoneId, CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT pg_advisory_xact_lock(1, to_char(day_value, 'YYYYMMDD')::integer)
            FROM (
                SELECT DISTINCT COALESCE(
                    business_date,
                    try_order_number_day(order_number),
                    (order_date AT TIME ZONE {timeZoneId})::date) AS day_value
                FROM orders
            ) days
            ORDER BY day_value
            """, cancellationToken);
}
