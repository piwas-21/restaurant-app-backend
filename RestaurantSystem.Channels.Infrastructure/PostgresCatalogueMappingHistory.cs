using System.Text.Json;
using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresCatalogueMappingHistory(NpgsqlDataSource source) : ICatalogueMappingHistory
{
    public async Task<CataloguePublication?> FindVerified(AvailabilityBinding binding, string catalogueRevision, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT id, mapping_hash, source_revision, publication_revision, menu::text, previous_menu::text,
                state, provider_hash, verified_at, mapping_snapshot::text
            FROM channel_catalogue_publications
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND catalogue_revision = $4
                AND state = 'Verified' AND provider_hash IS NOT NULL AND verified_at IS NOT NULL
            ORDER BY sequence DESC LIMIT 1
            """);
        PostgresChannelAvailabilityJobs.Identity(command, binding with { CatalogueRevision = catalogueRevision });
        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        if (!await row.ReadAsync(cancellationToken)) return null;
        return new(row.GetGuid(0), row.GetString(1), row.GetString(2), row.GetString(3),
            JsonSerializer.Deserialize<JsonElement>(row.GetString(4)), JsonSerializer.Deserialize<JsonElement>(row.GetString(5)),
            row.GetString(6), row.GetString(7), row.GetFieldValue<DateTimeOffset>(8),
            row.IsDBNull(9) ? null : JsonSerializer.Deserialize<JsonElement>(row.GetString(9)));
    }
}
