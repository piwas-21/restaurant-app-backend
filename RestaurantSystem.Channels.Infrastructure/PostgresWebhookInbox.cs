using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresWebhookInbox(NpgsqlDataSource dataSource) : IWebhookInbox
{
    public async Task<InboxWriteResult> Receive(WebhookReceipt receipt, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO channel_webhook_receipts
              (client_id, event_id, event_type, store_id, resource_id, event_time, body_hash, received_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            ON CONFLICT (client_id, event_id) DO NOTHING
            """;
        insert.Parameters.Add(new() { Value = receipt.ClientId });
        insert.Parameters.Add(new() { Value = receipt.EventId });
        insert.Parameters.Add(new() { Value = receipt.EventType });
        insert.Parameters.Add(new() { Value = receipt.StoreId });
        insert.Parameters.Add(new() { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar, Value = (object?)receipt.ResourceId ?? DBNull.Value });
        insert.Parameters.Add(new() { Value = receipt.EventTime });
        insert.Parameters.Add(new() { Value = receipt.BodyHash });
        insert.Parameters.Add(new() { Value = receipt.ReceivedAt });
        if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            return InboxWriteResult.Stored;

        // A concurrent insert has committed before ON CONFLICT returns. This second statement
        // takes a fresh READ COMMITTED snapshot and observes that row without an in-process lock.
        await using var existing = connection.CreateCommand();
        existing.CommandText = "SELECT body_hash FROM channel_webhook_receipts WHERE client_id = $1 AND event_id = $2";
        existing.Parameters.Add(new() { Value = receipt.ClientId });
        existing.Parameters.Add(new() { Value = receipt.EventId });
        var hash = await existing.ExecuteScalarAsync(cancellationToken) as string;
        return string.Equals(hash, receipt.BodyHash, StringComparison.Ordinal)
            ? InboxWriteResult.Duplicate : InboxWriteResult.Conflict;
    }
}
