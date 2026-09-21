using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using RestaurantSystem.Infrastructure.Persistence.Migrations;

namespace RestaurantSystem.IntegrationTests.Infrastructure;

public sealed class OrderRoutingRequirementMigrationTests
{
    [Fact]
    public void Up_backfills_historical_cashier_routes_as_optional()
    {
        var builder = new MigrationBuilder("Npgsql");
        new ExposedMigration().Apply(builder);

        var column = builder.Operations.OfType<AddColumnOperation>()
            .Single(operation => operation.Name == "is_required");
        column.DefaultValue.Should().Be(true);

        var backfill = builder.Operations.OfType<SqlOperation>().Single().Sql;
        backfill.Should().Contain("WHEN \"target\" = 'Cashier' THEN FALSE");
        backfill.Should().Contain("ELSE TRUE");
    }

    private sealed class ExposedMigration : AddOrderRoutingRequirement
    {
        public void Apply(MigrationBuilder builder) => Up(builder);
    }
}
