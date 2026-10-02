using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresChannelAvailabilityOverrides(NpgsqlDataSource source) : IChannelAvailabilityOverrides
{
    public async Task<ChannelAvailabilityOverride?> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT paused, paused_until, actor_id, updated_at FROM channel_availability_overrides
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3
            """);
        Identity(command, binding);
        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        if (!await row.ReadAsync(cancellationToken)) return null;
        DateTimeOffset? pausedUntil = row.IsDBNull(1) ? null : row.GetFieldValue<DateTimeOffset>(1);
        return new(row.GetBoolean(0), pausedUntil, row.GetGuid(2), row.GetFieldValue<DateTimeOffset>(3));
    }

    public async Task<ChannelAvailabilityOverride> Set(AvailabilityBinding binding, bool isPaused,
        DateTimeOffset? pausedUntil, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (actorId == Guid.Empty || isPaused && pausedUntil is { } until && until <= now
            || !isPaused && pausedUntil is not null) throw new ArgumentException("Availability override is invalid.");
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var anchor = new NpgsqlCommand("""
            INSERT INTO channel_availability_bindings(client_id, store_id, tenant_id) VALUES ($1, $2, $3)
            ON CONFLICT DO NOTHING
            """, connection, transaction))
        {
            Identity(anchor, binding);
            await anchor.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var command = new NpgsqlCommand("""
            INSERT INTO channel_availability_overrides(client_id, store_id, tenant_id, paused, paused_until, actor_id, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7)
            ON CONFLICT (client_id, store_id, tenant_id) DO UPDATE SET
                paused = EXCLUDED.paused, paused_until = EXCLUDED.paused_until,
                actor_id = EXCLUDED.actor_id, updated_at = EXCLUDED.updated_at
            """, connection, transaction))
        {
            Identity(command, binding); command.Parameters.AddWithValue(isPaused);
            command.Parameters.AddWithValue((object?)pausedUntil?.ToUniversalTime() ?? DBNull.Value);
            command.Parameters.AddWithValue(actorId); command.Parameters.AddWithValue(now.ToUniversalTime());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(isPaused, pausedUntil, actorId, now);
    }

    private static void Identity(NpgsqlCommand command, AvailabilityBinding binding)
    {
        command.Parameters.AddWithValue(binding.ClientId); command.Parameters.AddWithValue(binding.StoreId);
        command.Parameters.AddWithValue(binding.TenantId);
    }
}
