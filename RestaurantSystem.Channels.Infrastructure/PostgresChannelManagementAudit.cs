using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresChannelManagementAudit(NpgsqlDataSource source) : IChannelManagementAudit
{
    public async Task Record(AvailabilityBinding binding, Guid actorId, string action, string resultCode,
        Guid? operationId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var anchor = new NpgsqlCommand("""
            INSERT INTO channel_availability_bindings(client_id, store_id, tenant_id) VALUES ($1, $2, $3)
            ON CONFLICT DO NOTHING
            """, connection, transaction))
        {
            Identity(anchor, binding); await anchor.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var command = new NpgsqlCommand("""
            INSERT INTO channel_management_audit(client_id, store_id, tenant_id, actor_id, action, result_code, operation_id, occurred_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """, connection, transaction);
        Identity(command, binding); command.Parameters.AddWithValue(actorId);
        command.Parameters.AddWithValue(Bounded(action, 40)); command.Parameters.AddWithValue(Bounded(resultCode, 48));
        command.Parameters.AddWithValue((object?)operationId ?? DBNull.Value); command.Parameters.AddWithValue(occurredAt.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChannelManagementAuditRecord>> Read(AvailabilityBinding binding,
        long? beforeSequence, int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 51) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var command = source.CreateCommand("""
            SELECT sequence, actor_id, action, result_code, operation_id, occurred_at
            FROM channel_management_audit WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3
              AND ($4::bigint IS NULL OR sequence < $4)
            ORDER BY sequence DESC LIMIT $5
            """);
        Identity(command, binding); command.Parameters.AddWithValue((object?)beforeSequence ?? DBNull.Value);
        command.Parameters.AddWithValue(limit);
        var records = new List<ChannelManagementAuditRecord>();
        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        while (await row.ReadAsync(cancellationToken)) records.Add(new(row.GetInt64(0), row.GetGuid(1), row.GetString(2),
            row.GetString(3), row.IsDBNull(4) ? null : row.GetGuid(4), row.GetFieldValue<DateTimeOffset>(5)));
        return records;
    }

    private static string Bounded(string value, int maximum)
        => value.Length is > 0 && value.Length <= maximum && !value.Any(char.IsControl)
            ? value : throw new ArgumentException("Management audit code is invalid.");

    private static void Identity(NpgsqlCommand command, AvailabilityBinding binding)
    {
        command.Parameters.AddWithValue(binding.ClientId); command.Parameters.AddWithValue(binding.StoreId);
        command.Parameters.AddWithValue(binding.TenantId);
    }
}
