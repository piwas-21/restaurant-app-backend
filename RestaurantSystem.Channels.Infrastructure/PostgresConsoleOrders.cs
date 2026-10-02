using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed partial class PostgresConsoleRepository
{
    public async Task<IReadOnlyList<WebhookReceipt>> RecentReceipts(string clientId, Guid storeId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT event_id, event_type, resource_id, event_time, body_hash, received_at
            FROM channel_webhook_receipts WHERE client_id = $1 AND store_id = $2
            ORDER BY received_at DESC LIMIT 50
            """);
        Add(command, [clientId, storeId]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<WebhookReceipt>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(clientId, reader.GetString(0), reader.GetString(1), storeId,
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt64(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
        return rows;
    }

    public async Task<bool> HasOrder(string clientId, Guid storeId, string orderId, CancellationToken cancellationToken)
    {
        if (await Scalar("""
            SELECT EXISTS(SELECT 1 FROM channel_webhook_receipts
              WHERE client_id = $1 AND store_id = $2 AND resource_id = $3
              AND event_type IN ('orders.notification', 'orders.scheduled.notification', 'orders.release', 'orders.cancel'))
            """, cancellationToken, clientId, storeId, orderId) is true) return true;
        // Ingress-only/older bridge deployments remain readable before the additive recovery migration.
        if (!await HasRecoverySchema(cancellationToken) || !Guid.TryParseExact(orderId, "D", out var id)) return false;
        return await Scalar("""
            SELECT EXISTS(SELECT 1 FROM channel_import_jobs WHERE client_id = $1 AND store_id = $2
              AND order_id = $3 AND discovery_source = 'provider_poll' AND discovery_hash IS NOT NULL)
            """, cancellationToken, clientId, storeId, id) is true;
    }

    public async Task<DateTimeOffset?> RecoveryEnrollment(string clientId, Guid storeId, Guid orderId, CancellationToken cancellationToken)
    {
        if (!await HasRecoverySchema(cancellationToken)) return null;
        var value = await Scalar("""
            SELECT recovery_enrolled_at FROM channel_import_jobs WHERE client_id = $1 AND store_id = $2
              AND order_id = $3 AND discovery_source = 'provider_poll'
            """, cancellationToken, clientId, storeId, orderId);
        return value is DateTime date ? new DateTimeOffset(date) : null;
    }

    private async Task<bool> HasRecoverySchema(CancellationToken cancellationToken)
        => await Scalar("""
            SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema = 'public'
              AND table_name = 'channel_import_jobs' AND column_name = 'recovery_enrolled_at')
            """, cancellationToken) is true;

    public async Task<bool> ClaimAction(string clientId, Guid storeId, string orderId, string action, CancellationToken cancellationToken)
        => await Execute("""
            INSERT INTO channel_sandbox_order_actions (client_id, store_id, order_id, action, state)
            VALUES ($1, $2, $3, $4, 'Pending')
            ON CONFLICT (client_id, store_id, order_id) DO UPDATE
            SET action = EXCLUDED.action, state = 'Pending', updated_at = now()
            WHERE channel_sandbox_order_actions.state = 'Failed'
            """, cancellationToken, clientId, storeId, Guid.Parse(orderId), action) == 1;

    public async Task FinishAction(string clientId, Guid storeId, string orderId, string state, CancellationToken cancellationToken)
        => await Execute("""
            UPDATE channel_sandbox_order_actions SET state = $4, updated_at = now()
            WHERE client_id = $1 AND store_id = $2 AND order_id = $3
            """, cancellationToken, clientId, storeId, Guid.Parse(orderId), state);

    public async Task<OrderAction?> FindAction(string clientId, Guid storeId, string orderId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT action, state FROM channel_sandbox_order_actions WHERE client_id = $1 AND store_id = $2 AND order_id = $3
            """);
        Add(command, [clientId, storeId, Guid.Parse(orderId)]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.GetString(0), reader.GetString(1)) : null;
    }
}
