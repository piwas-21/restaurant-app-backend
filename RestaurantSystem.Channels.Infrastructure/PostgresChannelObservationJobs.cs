using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresChannelObservationJobs(NpgsqlDataSource source) : IChannelObservationJobs
{
    public async Task<ChannelObservationJob?> Claim(string clientId, Guid storeId, string tenantId, CancellationToken cancellationToken)
    {
        await using var discover = source.CreateCommand("""
            INSERT INTO channel_order_observations(client_id,store_id,order_id,tenant_id,tenant_order_id)
            SELECT client_id,store_id,order_id,tenant_id,tenant_order_id FROM channel_import_jobs
            WHERE client_id=$1 AND store_id=$2 AND tenant_id=$3 AND state='Imported'
            ON CONFLICT (client_id,store_id,order_id) DO NOTHING
            """);
        Add(discover, clientId, storeId, tenantId); await discover.ExecuteNonQueryAsync(cancellationToken);
        await using var claim = source.CreateCommand("""
            WITH candidate AS (
              SELECT observation.client_id,observation.store_id,observation.order_id
              FROM channel_order_observations AS observation
              JOIN channel_import_jobs AS job USING (client_id,store_id,order_id)
              WHERE observation.client_id=$1 AND observation.store_id=$2 AND observation.tenant_id=$3
                AND job.state='Imported' AND job.tenant_id=observation.tenant_id AND job.tenant_order_id=observation.tenant_order_id
                AND NOT terminal AND observation.available_at<=now()
                AND (observation.lease_until IS NULL OR observation.lease_until<=now())
              ORDER BY observation.available_at,observation.order_id FOR UPDATE OF observation SKIP LOCKED LIMIT 1
            )
            UPDATE channel_order_observations AS observation SET lease_id=$4,lease_until=now()+interval '2 minutes'
            FROM candidate AS c WHERE observation.client_id=c.client_id AND observation.store_id=c.store_id AND observation.order_id=c.order_id
            RETURNING observation.order_id,observation.tenant_order_id
            """);
        var lease = Guid.NewGuid(); Add(claim, clientId, storeId, tenantId, lease);
        await using var reader = await claim.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(clientId, storeId, reader.GetGuid(0), tenantId, reader.GetGuid(1), lease) : null;
    }

    public async Task<bool> Finish(ChannelObservationJob job, string? canonicalState, string? canonicalHash,
        DateTimeOffset? observedAt, bool terminal, DateTimeOffset availableAt, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            UPDATE channel_order_observations AS observation SET lease_until=NULL,available_at=$7,
              canonical_state=COALESCE($8,canonical_state),canonical_hash=COALESCE($9,canonical_hash),
              observed_at=COALESCE($10,observed_at),terminal=$11
            WHERE client_id=$1 AND store_id=$2 AND order_id=$3 AND tenant_id=$4 AND tenant_order_id=$5
              AND lease_id=$6 AND lease_until>now() AND NOT terminal
              AND EXISTS (SELECT 1 FROM channel_import_jobs AS job
                WHERE job.client_id=observation.client_id AND job.store_id=observation.store_id AND job.order_id=observation.order_id
                  AND job.tenant_id=observation.tenant_id AND job.tenant_order_id=observation.tenant_order_id AND job.state='Imported')
            """);
        Add(command, job.ClientId, job.StoreId, job.ExternalOrderId, job.TenantId, job.TenantOrderId, job.LeaseId, availableAt.ToUniversalTime());
        command.Parameters.Add(new() { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = (object?)canonicalState ?? DBNull.Value });
        command.Parameters.Add(new() { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Char, Value = (object?)canonicalHash ?? DBNull.Value });
        command.Parameters.Add(new() { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.TimestampTz, Value = (object?)observedAt?.ToUniversalTime() ?? DBNull.Value });
        command.Parameters.Add(new() { Value = terminal });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static void Add(NpgsqlCommand command, params object[] values)
    {
        foreach (var value in values) command.Parameters.Add(new() { Value = value });
    }
}
