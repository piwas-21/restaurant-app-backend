using RestaurantSystem.Channels.Domain;
using Npgsql;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresChannelOrderLinks(NpgsqlDataSource source) : IChannelOrderLinks
{
    public async Task<bool> IsImported(string clientId, Guid storeId, string tenantId, Guid externalOrderId, Guid tenantOrderId, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT EXISTS (SELECT 1 FROM channel_import_jobs
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND order_id = $4
              AND tenant_order_id = $5 AND state = 'Imported')
            """);
        command.Parameters.AddWithValue(clientId);
        command.Parameters.AddWithValue(storeId);
        command.Parameters.AddWithValue(tenantId);
        command.Parameters.AddWithValue(externalOrderId);
        command.Parameters.AddWithValue(tenantOrderId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
