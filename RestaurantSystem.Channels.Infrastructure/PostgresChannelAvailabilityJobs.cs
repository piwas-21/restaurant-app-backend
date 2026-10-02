using Npgsql;
using NpgsqlTypes;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresChannelAvailabilityJobs(NpgsqlDataSource source) : IChannelAvailabilityJobs
{
    internal const string LockKey = "hashtextextended('channel-availability:' || $1 || ':' || $2::text, 0)";

    public async Task<IChannelAvailabilityLease?> TryLease(AvailabilityBinding binding, CancellationToken cancellationToken)
    {
        var connection = await source.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(" + LockKey + ")", connection);
            Identity(command, binding, false);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
                return new PostgresAvailabilityLease(connection, binding);
            await connection.DisposeAsync();
            return null;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<IReadOnlyList<ChannelAvailabilityState>> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT provider_item_id, source_revision, desired_available, source_reason, state,
                   observed_available, provider_hash, verified_at
            FROM channel_availability_states
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND catalogue_revision = $4
            ORDER BY provider_item_id LIMIT 200
            """);
        Identity(command, binding);
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ChannelAvailabilityState>();
        while (await rows.ReadAsync(cancellationToken))
            result.Add(new(rows.GetString(0), rows.GetString(1), rows.GetBoolean(2), rows.GetString(3), rows.GetString(4),
                rows.IsDBNull(5) ? null : rows.GetBoolean(5), rows.IsDBNull(6) ? null : rows.GetString(6),
                rows.IsDBNull(7) ? null : rows.GetFieldValue<DateTimeOffset>(7)));
        return result;
    }

    internal static void Identity(NpgsqlCommand command, AvailabilityBinding binding, bool complete = true)
    {
        command.Parameters.AddWithValue(binding.ClientId); command.Parameters.AddWithValue(binding.StoreId);
        if (!complete) return;
        command.Parameters.AddWithValue(binding.TenantId); command.Parameters.AddWithValue(binding.CatalogueRevision);
    }

    internal static void Nullable(NpgsqlCommand command, NpgsqlDbType type, object? value)
        => command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = type, Value = value ?? DBNull.Value });
}
