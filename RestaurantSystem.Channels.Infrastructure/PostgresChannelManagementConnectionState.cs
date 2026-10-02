using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresChannelManagementConnectionState(NpgsqlDataSource source) : IChannelManagementConnectionState
{
    public async Task<ChannelManagementConnectionState> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT disconnected, actor_id, updated_at FROM channel_management_connections
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3
            """);
        Identity(command, binding); await using var row = await command.ExecuteReaderAsync(cancellationToken);
        return await row.ReadAsync(cancellationToken)
            ? new(row.GetBoolean(0), row.IsDBNull(1) ? null : row.GetGuid(1), row.IsDBNull(2) ? null : row.GetFieldValue<DateTimeOffset>(2))
            : new(false, null, null);
    }

    public async Task<ChannelManagementConnectionState> Set(AvailabilityBinding binding, bool isDisconnected,
        Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Management actor is invalid.");
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var anchor = new NpgsqlCommand("""
            INSERT INTO channel_availability_bindings(client_id, store_id, tenant_id) VALUES ($1, $2, $3)
            ON CONFLICT DO NOTHING
            """, connection, transaction))
        {
            Identity(anchor, binding); await anchor.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var command = new NpgsqlCommand("""
            INSERT INTO channel_management_connections(client_id, store_id, tenant_id, disconnected, actor_id, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (client_id, store_id, tenant_id) DO UPDATE SET
                disconnected = EXCLUDED.disconnected, actor_id = EXCLUDED.actor_id, updated_at = EXCLUDED.updated_at
            """, connection, transaction))
        {
            Identity(command, binding); command.Parameters.AddWithValue(isDisconnected);
            command.Parameters.AddWithValue(actorId); command.Parameters.AddWithValue(now.ToUniversalTime());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(isDisconnected, actorId, now);
    }

    private static void Identity(NpgsqlCommand command, AvailabilityBinding binding)
    {
        command.Parameters.AddWithValue(binding.ClientId); command.Parameters.AddWithValue(binding.StoreId);
        command.Parameters.AddWithValue(binding.TenantId);
    }
}
