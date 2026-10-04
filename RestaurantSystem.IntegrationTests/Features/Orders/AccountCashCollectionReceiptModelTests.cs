using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;

namespace RestaurantSystem.IntegrationTests.Features.Orders;

public sealed class AccountCashCollectionReceiptModelTests
{
    [Fact]
    public void Receipt_schema_metadata_maps_bounded_adjustment_and_staff_role_without_opening_a_database()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql()
            .Options;
        using var context = new ApplicationDbContext(options);

        var entity = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(AccountCashCollectionReceipt));
        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("account_cash_collection_receipts");
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var role = entity.FindProperty(nameof(AccountCashCollectionReceipt.ActorRole));
        role.Should().NotBeNull();
        role!.GetColumnName(table).Should().Be("actor_role");
        role.GetTypeMapping().Converter!.ProviderClrType.Should().Be<string>();

        entity.GetIndexes().Single(index => index.Properties
            .Any(property => property.Name == nameof(AccountCashCollectionReceipt.AttemptId)))
            .IsUnique.Should().BeTrue();
        entity.GetForeignKeys().Single(foreignKey => foreignKey.Properties
            .Any(property => property.Name == nameof(AccountCashCollectionReceipt.AttemptId)))
            .DeleteBehavior.Should().Be(DeleteBehavior.Restrict);

        var constraint = entity.GetCheckConstraints()
            .Single(value => value.Name == "ck_account_cash_collection_receipt_shape");

        constraint.Sql.Should().Contain("adjustment_minor BETWEEN -2 AND 2")
            .And.Contain("actor_role IN ('Admin','Cashier','Server')");
    }
}
