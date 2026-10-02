using Npgsql;
using NpgsqlTypes;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

internal sealed class PostgresAvailabilityLease(NpgsqlConnection connection, AvailabilityBinding binding) : IChannelAvailabilityLease
{
    public async Task<bool> Queue(string sourceRevision, IReadOnlyList<ChannelAvailabilityDesired> items,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await Bind(transaction, cancellationToken)) return false;
        foreach (var item in items)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO channel_availability_states (client_id, store_id, tenant_id, catalogue_revision,
                    provider_item_id, source_revision, desired_available, source_reason, updated_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                ON CONFLICT (client_id, store_id, catalogue_revision, provider_item_id) DO UPDATE SET
                    source_revision = EXCLUDED.source_revision, desired_available = EXCLUDED.desired_available,
                    source_reason = EXCLUDED.source_reason, updated_at = EXCLUDED.updated_at,
                    state = CASE WHEN channel_availability_states.source_revision = EXCLUDED.source_revision
                        AND channel_availability_states.desired_available = EXCLUDED.desired_available
                        AND channel_availability_states.source_reason = EXCLUDED.source_reason
                        THEN channel_availability_states.state ELSE 'Pending' END,
                    verified_at = CASE WHEN channel_availability_states.source_revision = EXCLUDED.source_revision
                        AND channel_availability_states.desired_available = EXCLUDED.desired_available
                        THEN channel_availability_states.verified_at ELSE NULL END
                WHERE channel_availability_states.tenant_id = EXCLUDED.tenant_id
                """, connection, transaction);
            PostgresChannelAvailabilityJobs.Identity(command, binding);
            command.Parameters.AddWithValue(item.ProviderItemId); command.Parameters.AddWithValue(sourceRevision);
            command.Parameters.AddWithValue(item.Available); command.Parameters.AddWithValue(item.Reason); command.Parameters.AddWithValue(now);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) return false;
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> Bind(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand("""
            INSERT INTO channel_availability_bindings (client_id, store_id, tenant_id)
            VALUES ($1, $2, $3) ON CONFLICT (client_id, store_id) DO NOTHING
            """, connection, transaction);
        PostgresChannelAvailabilityJobs.Identity(insert, binding, false);
        insert.Parameters.AddWithValue(binding.TenantId);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await using var read = new NpgsqlCommand("SELECT tenant_id FROM channel_availability_bindings WHERE client_id = $1 AND store_id = $2", connection, transaction);
        PostgresChannelAvailabilityJobs.Identity(read, binding, false);
        return await read.ExecuteScalarAsync(cancellationToken) is string tenant && tenant == binding.TenantId;
    }

    public async Task<bool> Observe(string itemId, string sourceRevision, string state, bool? observedAvailable,
        string? providerHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE channel_availability_states SET state = $7, observed_available = $8,
                provider_hash = $9, verified_at = CASE WHEN $7 = 'Verified' THEN $10 ELSE NULL END, updated_at = $10
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND catalogue_revision = $4
                AND provider_item_id = $5 AND source_revision = $6
            """, connection);
        PostgresChannelAvailabilityJobs.Identity(command, binding);
        command.Parameters.AddWithValue(itemId); command.Parameters.AddWithValue(sourceRevision); command.Parameters.AddWithValue(state);
        PostgresChannelAvailabilityJobs.Nullable(command, NpgsqlDbType.Boolean, observedAvailable);
        PostgresChannelAvailabilityJobs.Nullable(command, NpgsqlDbType.Char, providerHash);
        command.Parameters.AddWithValue(now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(" + PostgresChannelAvailabilityJobs.LockKey + ")", connection);
            unlock.CommandTimeout = 5;
            PostgresChannelAvailabilityJobs.Identity(unlock, binding, false);
            await unlock.ExecuteScalarAsync();
        }
        finally { await connection.DisposeAsync(); }
    }
}
