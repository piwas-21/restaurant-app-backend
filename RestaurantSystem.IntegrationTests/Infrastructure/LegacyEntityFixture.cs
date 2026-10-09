using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>Seeds frozen published schemas without writing explicitly excluded later metadata.</summary>
internal static class LegacyEntityFixture
{
    internal static async Task InsertAsync<T>(ApplicationDbContext context, T entity, params string[] omittedColumns) where T : class
    {
        var entityType = context.Model.FindEntityType(typeof(T))!;
        var tableName = entityType.GetTableName()!;
        var table = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
        var columns = entityType.GetProperties()
            .Select(property => (Property: property, Name: property.GetColumnName(table)))
            .Where(value => value.Name is not null && !omittedColumns.Contains(value.Name))
            .ToArray();
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await context.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"INSERT INTO \"{tableName}\" ({string.Join(", ", columns.Select(value => $"\"{value.Name}\""))}) "
            + $"VALUES ({string.Join(", ", columns.Select((_, index) => $"@p{index}"))})";
        for (var index = 0; index < columns.Length; index++)
        {
            var property = columns[index].Property;
            var value = property.GetGetter().GetClrValue(entity);
            var converter = property.GetValueConverter() ?? property.GetTypeMapping().Converter;
            if (value is not null && converter is not null)
                value = converter.ConvertToProvider(value);
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"p{index}";
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        await command.ExecuteNonQueryAsync();
    }
}
