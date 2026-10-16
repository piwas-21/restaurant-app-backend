using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffBillCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "original_bill_charge_amount",
                table: "table_occupancy_recovery_dispositions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "bill_charge_rules_json",
                table: "RestaurantInfo",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<decimal>(
                name: "bill_charge_amount",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "bill_charges_json",
                table: "orders",
                type: "text",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "original_bill_charge_amount",
                table: "table_occupancy_recovery_dispositions");

            migrationBuilder.DropColumn(
                name: "bill_charge_rules_json",
                table: "RestaurantInfo");

            migrationBuilder.DropColumn(
                name: "bill_charge_amount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "bill_charges_json",
                table: "orders");
        }
    }
}
