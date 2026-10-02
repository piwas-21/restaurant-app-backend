using Npgsql;
using RestaurantSystem.Channels.Domain;

namespace RestaurantSystem.Channels.Infrastructure;

public sealed partial class PostgresConsoleRepository(NpgsqlDataSource dataSource) : IConsoleRepository
{
    private async Task<int> Execute(string sql, CancellationToken cancellationToken, params object[] values)
    {
        await using var command = dataSource.CreateCommand(sql);
        Add(command, values);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<object?> Scalar(string sql, CancellationToken cancellationToken, params object[] values)
    {
        await using var command = dataSource.CreateCommand(sql);
        Add(command, values);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static void Add(NpgsqlCommand command, object[] values)
    {
        foreach (var value in values) command.Parameters.Add(new() { Value = value });
    }
}
