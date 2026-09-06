using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnforceOpeningHoursToOrderTypeConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "enforce_opening_hours",
                table: "order_type_configurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill the pre-#448 behaviour per type: DineIn was the ONLY order type refused
            // outside working hours; takeaway and delivery were accepted at any hour and must
            // stay that way until a tenant turns the gate on. order_type: 1 = DineIn. Purely
            // additive — no column is altered or dropped, so Down() restores the old world with
            // no data restore.
            migrationBuilder.Sql(
                """
                UPDATE order_type_configurations
                SET enforce_opening_hours = TRUE
                WHERE order_type = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "enforce_opening_hours",
                table: "order_type_configurations");
        }
    }
}
