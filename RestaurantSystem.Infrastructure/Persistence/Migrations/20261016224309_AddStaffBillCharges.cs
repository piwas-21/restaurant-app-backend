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
            migrationBuilder.Sql("""
                LOCK TABLE orders, table_occupancy_recovery_dispositions IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM orders WHERE bill_charge_amount <> 0 OR bill_charges_json <> '[]')
                        OR EXISTS (SELECT 1 FROM table_occupancy_recovery_dispositions WHERE original_bill_charge_amount <> 0)
                    THEN
                        RAISE EXCEPTION 'Staff bill charge history must be retained' USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);
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
