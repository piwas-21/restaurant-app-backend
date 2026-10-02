using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresCreatedOrderDiscoveries(NpgsqlDataSource source) : ICreatedOrderDiscoveries
{
    public async Task Record(string clientId, Guid storeId, string tenantId, string catalogueRevision, DateTimeOffset enrolledAt,
        IReadOnlyList<CreatedOrderCandidate> orders, CancellationToken cancellationToken)
    {
        if (orders.Count == 0) return;
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var order in orders)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO channel_import_jobs (client_id, store_id, order_id, tenant_id, catalogue_revision,
                  discovery_source, discovery_hash, created_at, recovery_enrolled_at)
                VALUES ($1, $2, $3, $4, $5, 'provider_poll', $6, $7, $8)
                ON CONFLICT (client_id, store_id, order_id) DO NOTHING
                """, connection, transaction);
            foreach (var value in new object[] { clientId, storeId, order.OrderId, tenantId, catalogueRevision,
                order.ResponseHash, order.PlacedAt.ToUniversalTime(), enrolledAt.ToUniversalTime() }) command.Parameters.Add(new() { Value = value });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
