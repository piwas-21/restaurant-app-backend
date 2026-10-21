using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace RestaurantSystem.IntegrationTests.Features.TableGuestVisits;

internal sealed class CustomerTableRoundErasureInterceptor : DbCommandInterceptor
{
    internal Func<Task>? ErasureLocked { get; set; }
    internal TaskCompletionSource<int> ErasureAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<int> NativeAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool Armed { get; set; }
    private static bool UserLock(DbCommand command, string strength) =>
        command.CommandText.Contains("FROM \"Users\"", StringComparison.Ordinal)
        && command.CommandText.Contains(strength, StringComparison.Ordinal);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Armed && command.Connection is NpgsqlConnection connection)
        {
            if (UserLock(command, "FOR UPDATE")) ErasureAttempted.TrySetResult(connection.ProcessID);
            if (UserLock(command, "FOR NO KEY UPDATE")) NativeAttempted.TrySetResult(connection.ProcessID);
        }
        return ValueTask.FromResult(result);
    }
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
        CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        if (Armed && UserLock(command, "FOR UPDATE") && ErasureLocked is { } locked) await locked();
        return result;
    }
}
