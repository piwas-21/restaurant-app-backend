using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed partial class PostgresConsoleRepository
{
    public async Task SaveToken(StoredToken token, CancellationToken cancellationToken)
        => await Execute("""
            INSERT INTO channel_sandbox_tokens (client_id, store_id, kind, token_cipher, expires_at)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (client_id, store_id, kind) DO UPDATE
            SET token_cipher = EXCLUDED.token_cipher, expires_at = EXCLUDED.expires_at
            """, cancellationToken, token.ClientId, token.StoreId, token.Kind, token.Cipher, token.ExpiresAt);

    public async Task<StoredToken?> FindToken(string clientId, Guid storeId, string kind, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT token_cipher, expires_at FROM channel_sandbox_tokens WHERE client_id = $1 AND store_id = $2 AND kind = $3
            """);
        Add(command, [clientId, storeId, kind]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(clientId, storeId, kind, reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)) : null;
    }
}
