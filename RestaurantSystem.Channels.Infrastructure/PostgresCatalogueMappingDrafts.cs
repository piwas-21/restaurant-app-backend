using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed class PostgresCatalogueMappingDrafts(NpgsqlDataSource source) : ICatalogueMappingDrafts
{
    public async Task<CatalogueMappingDraft?> Read(AvailabilityBinding binding, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand("""
            SELECT draft_revision::text, mapping_revision, mapping_snapshot::text, actor_id, updated_at
            FROM channel_catalogue_mapping_drafts WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3
            """);
        Identity(command, binding);
        await using var row = await command.ExecuteReaderAsync(cancellationToken);
        if (!await row.ReadAsync(cancellationToken)) return null;
        return new(row.GetString(0), row.GetString(1), JsonSerializer.Deserialize<JsonElement>(row.GetString(2)),
            row.GetGuid(3), row.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task<bool> Save(AvailabilityBinding binding, CatalogueMappingDraft draft,
        string? expectedRevision, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(draft.Revision, "D", out var revision) || revision == Guid.Empty
            || draft.ActorId == Guid.Empty || draft.MappingRevision.Length != 64
            || !draft.MappingRevision.All(Uri.IsHexDigit)) throw new ArgumentException("Catalogue mapping draft is invalid.");
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var anchor = new NpgsqlCommand("""
            INSERT INTO channel_availability_bindings(client_id, store_id, tenant_id) VALUES ($1, $2, $3)
            ON CONFLICT DO NOTHING
            """, connection, transaction))
        {
            Identity(anchor, binding);
            await anchor.ExecuteNonQueryAsync(cancellationToken);
        }
        var changed = expectedRevision is null
            ? await Insert(connection, transaction, binding, draft, revision, cancellationToken)
            : await Update(connection, transaction, binding, draft, revision, expectedRevision, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }

    private static async Task<bool> Insert(NpgsqlConnection connection, NpgsqlTransaction transaction,
        AvailabilityBinding binding, CatalogueMappingDraft draft, Guid revision, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO channel_catalogue_mapping_drafts(client_id, store_id, tenant_id, draft_revision,
                mapping_revision, mapping_snapshot, actor_id, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8) ON CONFLICT DO NOTHING
            """, connection, transaction);
        Identity(command, binding); command.Parameters.AddWithValue(revision);
        command.Parameters.AddWithValue(draft.MappingRevision); command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, draft.Snapshot.GetRawText());
        command.Parameters.AddWithValue(draft.ActorId); command.Parameters.AddWithValue(draft.UpdatedAt.ToUniversalTime());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<bool> Update(NpgsqlConnection connection, NpgsqlTransaction transaction,
        AvailabilityBinding binding, CatalogueMappingDraft draft, Guid revision, string expectedRevision,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(expectedRevision, "D", out var expected)) return false;
        await using var command = new NpgsqlCommand("""
            UPDATE channel_catalogue_mapping_drafts SET draft_revision = $4, mapping_revision = $5,
                mapping_snapshot = $6, actor_id = $7, updated_at = $8
            WHERE client_id = $1 AND store_id = $2 AND tenant_id = $3 AND draft_revision = $9
            """, connection, transaction);
        Identity(command, binding); command.Parameters.AddWithValue(revision);
        command.Parameters.AddWithValue(draft.MappingRevision); command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, draft.Snapshot.GetRawText());
        command.Parameters.AddWithValue(draft.ActorId); command.Parameters.AddWithValue(draft.UpdatedAt.ToUniversalTime());
        command.Parameters.AddWithValue(expected);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static void Identity(NpgsqlCommand command, AvailabilityBinding binding)
    {
        command.Parameters.AddWithValue(binding.ClientId); command.Parameters.AddWithValue(binding.StoreId);
        command.Parameters.AddWithValue(binding.TenantId);
    }
}
