using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresChannelImportJobs(NpgsqlDataSource dataSource) : IChannelImportJobs
{
    public async Task ExpirePayloads(CancellationToken cancellationToken)
    {
        // The additive bridge migration is applied explicitly. Older ingress-only deployments
        // have no import table yet; cleanup must stay compatible while forwarding is disabled.
        await using var exists = dataSource.CreateCommand("SELECT to_regclass('public.channel_import_jobs') IS NOT NULL");
        if (await exists.ExecuteScalarAsync(cancellationToken) is not true) return;
        await using var command = dataSource.CreateCommand("""
            UPDATE channel_import_jobs SET encrypted_request = NULL, payload_expires_at = NULL,
              state = 'Quarantined', last_code = 'PayloadExpired', lease_until = NULL, updated_at = now()
            WHERE encrypted_request IS NOT NULL AND payload_expires_at <= now()
            """);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task Discover(string clientId, Guid storeId, string tenantId, string revision, DateTimeOffset enrolledAt, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO channel_import_jobs (client_id, store_id, order_id, tenant_id, catalogue_revision)
            SELECT DISTINCT client_id, store_id, resource_id::uuid, $3, $4
            FROM channel_webhook_receipts
            WHERE client_id = $1 AND store_id = $2 AND received_at >= $5
              AND event_type = 'orders.notification'
              AND resource_id ~ '^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$'
            ON CONFLICT (client_id, store_id, order_id) DO NOTHING
            """);
        Add(command, clientId, storeId, tenantId, revision, enrolledAt.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ChannelImportJob?> Claim(string clientId, Guid storeId, string tenantId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            WITH candidate AS (
              SELECT client_id, store_id, order_id FROM channel_import_jobs
              WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3
                AND state IN ('Pending', 'Prepared') AND available_at <= now()
                AND (lease_until IS NULL OR lease_until <= now())
              ORDER BY created_at FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE channel_import_jobs AS job SET lease_id = $4, lease_until = now() + interval '2 minutes',
              attempts = attempts + 1, updated_at = now()
            FROM candidate AS c WHERE job.client_id = c.client_id AND job.store_id = c.store_id AND job.order_id = c.order_id
            RETURNING job.order_id, job.catalogue_revision, job.encrypted_request, job.payload_expires_at
            """);
        var lease = Guid.NewGuid(); Add(command, clientId, storeId, tenantId, lease);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var encryptedRequest = reader.IsDBNull(2) ? null : reader.GetString(2);
        DateTimeOffset? expiresAt = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3);
        return new(clientId, storeId, reader.GetGuid(0), tenantId, reader.GetString(1), lease,
            encryptedRequest, expiresAt);
    }

    public Task<bool> Prepare(ChannelImportJob job, string ciphertext, string requestHash, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        => Update(job, """
            SET encrypted_request = $5, request_hash = $6, payload_expires_at = $7, state = 'Prepared', updated_at = now()
            """, "AND encrypted_request IS NULL", cancellationToken, ciphertext, requestHash, expiresAt.ToUniversalTime());

    public Task<bool> Imported(ChannelImportJob job, Guid tenantOrderId, CancellationToken cancellationToken)
        => Update(job, """
            SET tenant_order_id = $5, state = 'Imported', encrypted_request = NULL, payload_expires_at = NULL,
              lease_until = NULL, last_code = NULL, updated_at = now()
            """, "AND state = 'Prepared'", cancellationToken, tenantOrderId);

    public Task<bool> Defer(ChannelImportJob job, string code, DateTimeOffset availableAt, bool quarantine, CancellationToken cancellationToken)
        => Update(job, """
            SET last_code = $5, available_at = $6, lease_until = NULL, updated_at = now(),
              state = CASE WHEN $7 THEN 'Quarantined' ELSE state END,
              encrypted_request = CASE WHEN $7 THEN NULL ELSE encrypted_request END,
              payload_expires_at = CASE WHEN $7 THEN NULL ELSE payload_expires_at END
            """, "", cancellationToken, code, availableAt.ToUniversalTime(), quarantine);

    private async Task<bool> Update(ChannelImportJob job, string set, string additional, CancellationToken cancellationToken, params object[] values)
    {
        await using var command = dataSource.CreateCommand($"""
            UPDATE channel_import_jobs {set}
            WHERE client_id = $1 AND store_id = $2 AND order_id = $3 AND lease_id = $4
              AND tenant_id = ${5 + values.Length} AND lease_until > now() AND state IN ('Pending', 'Prepared') {additional}
            """);
        Add(command, [job.ClientId, job.StoreId, job.OrderId, job.LeaseId, .. values, job.TenantId]);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static void Add(NpgsqlCommand command, params object[] values)
    {
        foreach (var value in values) command.Parameters.Add(new() { Value = value });
    }
}
