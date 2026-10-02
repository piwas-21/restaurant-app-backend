using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresChannelImportStatus(NpgsqlDataSource source) : IChannelImportStatus
{
    public async Task<IReadOnlyList<ChannelImportStatus>> Read(string clientId, Guid storeId, string tenantId, ChannelImportCursor? cursor, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            WITH scoped AS (
                SELECT order_id, catalogue_revision, state, attempts, last_code, tenant_order_id, created_at, updated_at,
                    CASE WHEN state = 'Quarantined' THEN 0 WHEN last_code IS NOT NULL THEN 1
                        WHEN state <> 'Imported' THEN 2 ELSE 3 END AS priority
                FROM channel_import_jobs WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3
            )
            SELECT * FROM scoped WHERE $4 IS NULL OR (priority, created_at, order_id) > ($4, $5, $6)
            ORDER BY priority, created_at, order_id LIMIT $7
            """);
        command.Parameters.AddWithValue(clientId); command.Parameters.AddWithValue(storeId); command.Parameters.AddWithValue(tenantId);
        PostgresChannelAvailabilityJobs.Nullable(command, NpgsqlTypes.NpgsqlDbType.Integer, cursor?.Priority);
        PostgresChannelAvailabilityJobs.Nullable(command, NpgsqlTypes.NpgsqlDbType.TimestampTz, cursor?.CreatedAt.ToUniversalTime());
        PostgresChannelAvailabilityJobs.Nullable(command, NpgsqlTypes.NpgsqlDbType.Uuid, cursor?.OrderId);
        command.Parameters.AddWithValue(IChannelImportStatus.PageSize + 1);
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ChannelImportStatus>();
        while (await rows.ReadAsync(cancellationToken))
        {
            // Never return raw database error text or retained encrypted customer payload.
            var code = rows.IsDBNull(4) ? "" : rows.GetString(4);
            if (code is not ("" or "ContractRejected" or "DeliveryUncertain" or "PayloadExpired")) code = "ReviewRequired";
            result.Add(new(rows.GetGuid(0), rows.GetString(1), rows.GetString(2), rows.GetInt32(3), code,
                rows.IsDBNull(5) ? null : rows.GetGuid(5), rows.GetFieldValue<DateTimeOffset>(6), rows.GetFieldValue<DateTimeOffset>(7), rows.GetInt32(8)));
        }
        return result;
    }
}
