using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

/// <summary>Writes a #664-era snapshot under its published schema, omitting additive #146 metadata.</summary>
internal static class LegacyOrderBillingSnapshotFixture
{
    internal static async Task InsertAsync(ApplicationDbContext context, OrderBillingSnapshot snapshot)
    {
        var entityType = context.Model.FindEntityType(typeof(OrderBillingSnapshot))!;
        var table = StoreObjectIdentifier.Table("order_billing_snapshots", null);
        var columns = entityType.GetProperties()
            .Select(property => (Property: property, Name: property.GetColumnName(table)))
            .Where(value => value.Name is not null && value.Name != "earning_disposition")
            .ToArray();
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await context.Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"INSERT INTO order_billing_snapshots ({string.Join(", ", columns.Select(value => $"\"{value.Name}\""))}) "
            + $"VALUES ({string.Join(", ", columns.Select((_, index) => $"@p{index}"))})";
        for (var index = 0; index < columns.Length; index++)
        {
            var property = columns[index].Property;
            var value = property.GetGetter().GetClrValue(snapshot);
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
