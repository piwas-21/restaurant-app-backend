using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresTenantOAuthFlows(NpgsqlDataSource source) : ITenantOAuthFlows
{
    public async Task Create(TenantOAuthFlow flow, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var anchor = new NpgsqlCommand("""
            INSERT INTO channel_availability_bindings(client_id, store_id, tenant_id) VALUES ($1, $2, $3)
            ON CONFLICT DO NOTHING
            """, connection, transaction))
        {
            Identity(anchor, new(flow.ClientId, flow.StoreId, flow.TenantId, string.Empty));
            await anchor.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var command = new NpgsqlCommand("""
            INSERT INTO channel_tenant_oauth_flows(flow_id, client_id, store_id, tenant_id, actor_id,
                state_hash, verifier_cipher, enable_order_acceptance, status, created_at, expires_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'Pending', $9, $10)
            """, connection, transaction);
        command.Parameters.AddWithValue(flow.Id); Identity(command, new(flow.ClientId, flow.StoreId, flow.TenantId, string.Empty));
        command.Parameters.AddWithValue(flow.ActorId); command.Parameters.AddWithValue(flow.StateHash);
        command.Parameters.AddWithValue(flow.VerifierCipher); command.Parameters.AddWithValue(flow.EnableOrderAcceptance);
        command.Parameters.AddWithValue(flow.CreatedAt.ToUniversalTime());
        command.Parameters.AddWithValue(flow.ExpiresAt.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task<TenantOAuthFlow?> Read(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand(Select + " WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND flow_id = $4");
        Identity(command, binding); command.Parameters.AddWithValue(id);
        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        return await row.ReadAsync(cancellationToken) ? Map(row) : null;
    }

    public async Task<TenantOAuthFlow?> FindByStateHash(AvailabilityBinding binding, string stateHash,
        CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand(Select + " WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND state_hash = $4");
        Identity(command, binding); command.Parameters.AddWithValue(stateHash);
        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        return await row.ReadAsync(cancellationToken) ? Map(row) : null;
    }

    public async Task Expire(AvailabilityBinding binding, Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            UPDATE channel_tenant_oauth_flows
            SET status = CASE WHEN status = 'Pending' THEN 'Expired' ELSE 'Failed' END,
                error_code = CASE WHEN status = 'Pending' THEN 'AuthorizationExpired' ELSE 'ConnectionUnconfirmed' END,
                completed_at = $4, verifier_cipher = ''
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3
              AND flow_id = $5 AND status IN ('Pending', 'Processing') AND expires_at <= $4
            """);
        Identity(command, binding); command.Parameters.AddWithValue(now.ToUniversalTime()); command.Parameters.AddWithValue(id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CancelPending(AvailabilityBinding binding, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            UPDATE channel_tenant_oauth_flows
            SET status = 'Failed', error_code = 'ConnectionDisconnected', completed_at = $4, verifier_cipher = ''
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND status = 'Pending'
            """);
        Identity(command, binding); command.Parameters.AddWithValue(now.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<TenantOAuthFlow?> Claim(AvailabilityBinding binding, string stateHash, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            UPDATE channel_tenant_oauth_flows SET status = CASE WHEN expires_at > $5 THEN 'Processing' ELSE 'Expired' END,
                completed_at = CASE WHEN expires_at > $5 THEN NULL ELSE $5 END,
                error_code = CASE WHEN expires_at > $5 THEN NULL ELSE 'AuthorizationExpired' END,
                verifier_cipher = CASE WHEN expires_at > $5 THEN verifier_cipher ELSE '' END
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND state_hash = $4
              AND status = 'Pending'
            RETURNING flow_id, client_id, store_id, tenant_id, actor_id, state_hash, verifier_cipher,
                enable_order_acceptance, status, error_code, created_at, expires_at, completed_at
            """);
        Identity(command, binding); command.Parameters.AddWithValue(stateHash); command.Parameters.AddWithValue(now.ToUniversalTime());
        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        return await row.ReadAsync(cancellationToken) ? Map(row) : null;
    }

    public async Task<bool> FailPending(AvailabilityBinding binding, Guid id, string stateHash, string errorCode,
        DateTimeOffset completedAt, CancellationToken cancellationToken)
    {
        if (errorCode is not ("ConnectionOperationBusy" or "ConnectionDisconnected")) return false;
        await using var command = source.CreateCommand("""
            UPDATE channel_tenant_oauth_flows SET status = 'Failed', error_code = $6,
                completed_at = $7, verifier_cipher = ''
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND flow_id = $4
              AND state_hash = $5 AND status = 'Pending'
            """);
        Identity(command, binding); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(stateHash);
        command.Parameters.AddWithValue(errorCode); command.Parameters.AddWithValue(completedAt.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> Finish(AvailabilityBinding binding, Guid id, string status, string? errorCode,
        DateTimeOffset completedAt, CancellationToken cancellationToken)
    {
        if (status is not ("Connected" or "Failed" or "Expired") || errorCode?.Length > 48) return false;
        await using var command = source.CreateCommand("""
            UPDATE channel_tenant_oauth_flows SET status = $5, error_code = $6, completed_at = $7, verifier_cipher = ''
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND flow_id = $4 AND status = 'Processing'
            """);
        Identity(command, binding); command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue((object?)errorCode ?? DBNull.Value); command.Parameters.AddWithValue(completedAt.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private const string Select = """
        SELECT flow_id, client_id, store_id, tenant_id, actor_id, state_hash, verifier_cipher,
               enable_order_acceptance, status, error_code, created_at, expires_at, completed_at FROM channel_tenant_oauth_flows
        """;

    private static TenantOAuthFlow Map(NpgsqlDataReader row)
        => new(row.GetGuid(0), row.GetString(1), row.GetGuid(2), row.GetString(3), row.GetGuid(4), row.GetString(5),
            row.GetString(6), row.GetBoolean(7), row.GetString(8), row.IsDBNull(9) ? null : row.GetString(9),
            row.GetFieldValue<DateTimeOffset>(10), row.GetFieldValue<DateTimeOffset>(11),
            row.IsDBNull(12) ? null : row.GetFieldValue<DateTimeOffset>(12));

    private static void Identity(NpgsqlCommand command, AvailabilityBinding binding)
    {
        command.Parameters.AddWithValue(binding.ClientId); command.Parameters.AddWithValue(binding.StoreId);
        command.Parameters.AddWithValue(binding.TenantId);
    }
}
