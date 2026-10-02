using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresCataloguePublications(NpgsqlDataSource source) : ICataloguePublications
{
    public async Task<CataloguePublication?> Latest(AvailabilityBinding binding, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT id, mapping_hash, source_revision, publication_revision, menu::text, previous_menu::text,
                   state, provider_hash, verified_at, mapping_snapshot::text
            FROM channel_catalogue_publications
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3
            ORDER BY sequence DESC LIMIT 1
            """);
        Identity(command, binding);
        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        if (!await row.ReadAsync(cancellationToken)) return null;
        return Read(row);
    }

    public async Task<CataloguePublication?> Find(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT id, mapping_hash, source_revision, publication_revision, menu::text, previous_menu::text,
                   state, provider_hash, verified_at, mapping_snapshot::text
            FROM channel_catalogue_publications
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND id = $4
            """);
        Identity(command, binding); command.Parameters.AddWithValue(id);
        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        return await row.ReadAsync(cancellationToken) ? Read(row) : null;
    }

    private static CataloguePublication Read(NpgsqlDataReader row)
        => new(row.GetGuid(0), row.GetString(1), row.GetString(2), row.GetString(3),
            JsonSerializer.Deserialize<JsonElement>(row.GetString(4)), JsonSerializer.Deserialize<JsonElement>(row.GetString(5)),
            row.GetString(6), row.IsDBNull(7) ? null : row.GetString(7), row.IsDBNull(8) ? null : row.GetFieldValue<DateTimeOffset>(8),
            row.IsDBNull(9) ? null : JsonSerializer.Deserialize<JsonElement>(row.GetString(9)));

    public async Task<CataloguePublication> Begin(AvailabilityBinding binding, CataloguePublicationIntent intent,
        CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var anchor = new NpgsqlCommand("INSERT INTO channel_availability_bindings(client_id, store_id, tenant_id) VALUES ($1, $2, $3) ON CONFLICT DO NOTHING", connection, transaction))
        {
            anchor.Parameters.AddWithValue(binding.ClientId); anchor.Parameters.AddWithValue(binding.StoreId);
            anchor.Parameters.AddWithValue(binding.TenantId); await anchor.ExecuteNonQueryAsync(cancellationToken);
        }
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO channel_catalogue_publications(id, client_id, store_id, tenant_id, catalogue_revision,
                mapping_hash, source_revision, publication_revision, menu, previous_menu, mapping_snapshot)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
            """, connection, transaction);
        command.Parameters.AddWithValue(id); PostgresChannelAvailabilityJobs.Identity(command, binding);
        command.Parameters.AddWithValue(intent.MappingHash); command.Parameters.AddWithValue(intent.SourceRevision); command.Parameters.AddWithValue(intent.Revision);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, intent.Menu.GetRawText());
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, intent.PreviousMenu.GetRawText());
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, intent.MappingSnapshot is { } snapshot ? snapshot.GetRawText() : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return new(id, intent.MappingHash, intent.SourceRevision, intent.Revision, intent.Menu, intent.PreviousMenu, CataloguePublicationStates.Pending, null, null, intent.MappingSnapshot);
    }

    public async Task<bool> Verify(AvailabilityBinding binding, Guid id, string providerHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            UPDATE channel_catalogue_publications SET state = 'Verified', provider_hash = $6, verified_at = $7
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND catalogue_revision = $4 AND id = $5
              AND sequence = (SELECT max(sequence) FROM channel_catalogue_publications
                  WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3)
              AND state IN ('Pending', 'Verified')
            """);
        PostgresChannelAvailabilityJobs.Identity(command, binding); command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(providerHash); command.Parameters.AddWithValue(now.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> Abandon(AvailabilityBinding binding, Guid id, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            UPDATE channel_catalogue_publications SET state = 'Abandoned'
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND catalogue_revision = $4 AND id = $5
              AND state = 'Pending' AND sequence = (SELECT max(sequence) FROM channel_catalogue_publications
                  WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3)
            """);
        PostgresChannelAvailabilityJobs.Identity(command, binding); command.Parameters.AddWithValue(id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static void Identity(NpgsqlCommand command, AvailabilityBinding binding)
    {
        command.Parameters.AddWithValue(binding.ClientId); command.Parameters.AddWithValue(binding.StoreId);
        command.Parameters.AddWithValue(binding.TenantId);
    }
}
