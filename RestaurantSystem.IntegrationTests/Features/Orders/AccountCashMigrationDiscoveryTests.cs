using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountCashMigrationDiscoveryTests
{
    [Theory]
    [InlineData("20261004031747_AddAccountCashCollectionReceipts", typeof(AccountCashCollectionReceipt), "account_cash_collection_receipts")]
    [InlineData("20261004043011_AddAccountCashRefundEvidence", typeof(AccountCashRefundEvidence), "account_cash_refund_evidence")]
    public void Compiled_cash_migration_metadata_is_discoverable_without_database_access(
        string migrationId, Type entityType, string tableName)
    {
        using var context = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql().Options);
        var assembly = context.GetService<IMigrationsAssembly>();
        assembly.Migrations.Should().ContainKey(migrationId);
        var migrationType = assembly.Migrations[migrationId];
        migrationType.GetCustomAttribute<MigrationAttribute>()!.Id.Should().Be(migrationId);
        migrationType.GetCustomAttribute<DbContextAttribute>()!.ContextType.Should().Be<ApplicationDbContext>();
        var migration = assembly.CreateMigration(migrationType, context.Database.ProviderName!);
        migration.TargetModel.FindEntityType(entityType.FullName!)!.GetTableName().Should().Be(tableName);
        assembly.ModelSnapshot!.Model.FindEntityType(entityType.FullName!)!.GetTableName().Should().Be(tableName);
    }
}
