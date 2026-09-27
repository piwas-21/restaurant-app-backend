using System.Buffers.Binary;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem.Api.Common.Exceptions;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

public sealed class CatalogueImportLock(ApplicationDbContext context) : ICatalogueImportLock
{
    private const string LockNamespace = "sofra-catalogue-import:";
    private readonly HashSet<long> _heldKeys = [];
    private DbConnection? _connection;
    private bool _openedConnection;

    public async Task<IAsyncDisposable> AcquireAsync(string scope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 160)
        {
            throw new ArgumentException("A valid catalogue import lock scope is required", nameof(scope));
        }

        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
            _openedConnection = true;
        }

        _connection = connection;
        var key = LockKey(scope);
        if (!await TryLockAsync(connection, key, cancellationToken))
        {
            await CloseIfIdleAsync();
            throw new ConflictException("Another catalogue import is processing this source. Retry after it finishes.");
        }

        _heldKeys.Add(key);
        return new Lease(this, key);
    }

    private async Task ReleaseAsync(long key)
    {
        if (_connection is not null && _heldKeys.Remove(key))
        {
            await UnlockAsync(_connection, key);
        }

        await CloseIfIdleAsync();
    }

    private async Task CloseIfIdleAsync()
    {
        if (_heldKeys.Count != 0 || !_openedConnection)
        {
            return;
        }

        await context.Database.CloseConnectionAsync();
        _openedConnection = false;
        _connection = null;
    }

    private static async Task<bool> TryLockAsync(
        DbConnection connection,
        long key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@lock_key)";
        AddKey(command, key);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task UnlockAsync(DbConnection connection, long key)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@lock_key)";
        AddKey(command, key);
        await command.ExecuteScalarAsync(CancellationToken.None);
    }

    private static void AddKey(DbCommand command, long key)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
    }

    internal static long LockKey(string scope)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(LockNamespace + scope));
        return BinaryPrimitives.ReadInt64BigEndian(hash.AsSpan(0, sizeof(long)));
    }

    private sealed class Lease(CatalogueImportLock owner, long key) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(owner.ReleaseAsync(key));
    }
}
