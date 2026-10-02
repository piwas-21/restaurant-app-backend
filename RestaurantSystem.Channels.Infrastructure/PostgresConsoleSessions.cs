using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed partial class PostgresConsoleRepository
{
    public async Task CreateSession(string hash, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await Execute("DELETE FROM channel_console_sessions WHERE expires_at < now()", cancellationToken);
        await Execute("INSERT INTO channel_console_sessions (session_hash, expires_at) VALUES ($1, $2)", cancellationToken, hash, expiresAt);
    }

    public async Task<bool> SessionExists(string hash, DateTimeOffset now, CancellationToken cancellationToken)
        => await Scalar("SELECT EXISTS(SELECT 1 FROM channel_console_sessions WHERE session_hash = $1 AND expires_at > $2)", cancellationToken, hash, now) is true;

    public async Task EndSession(string hash, CancellationToken cancellationToken)
        => await Execute("DELETE FROM channel_console_sessions WHERE session_hash = $1", cancellationToken, hash);

    public async Task SaveAuthorization(AuthorizationState state, CancellationToken cancellationToken)
        => await Execute("""
            INSERT INTO channel_authorization_states (state_hash, session_hash, store_id, verifier_cipher, expires_at, enable_testing)
            VALUES ($1, $2, $3, $4, $5, $6)
            """, cancellationToken, state.Hash, state.SessionHash, state.StoreId, state.VerifierCipher, state.ExpiresAt, state.EnableTesting);

    public async Task<AuthorizationState?> ClaimAuthorization(string hash, string session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE channel_authorization_states SET claimed_at = $3
            WHERE state_hash = $1 AND session_hash = $2 AND expires_at > $3 AND claimed_at IS NULL
            RETURNING store_id, verifier_cipher, expires_at, enable_testing
            """);
        Add(command, [hash, session, now]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(hash, session, reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetBoolean(3)) : null;
    }
}
