using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStableTableIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "table_id",
                table: "table_service_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "table_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "table_label",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "table_number",
                table: "table_service_sessions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "table_number",
                table: "table_bill_payment_operations",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            // Backfill only when the legacy numeric label identifies exactly one configured
            // table. Non-numeric labels and any ambiguous mapping remain null for remediation;
            // table number is never treated as a safe identity in those cases.
            migrationBuilder.Sql("""
                UPDATE orders AS o
                SET table_id = t.id,
                    table_label = t.table_number
                FROM "Tables" AS t
                WHERE o.table_number IS NOT NULL
                  AND o.table_number::text ~ '^[0-9]+$'
                  AND t.table_number = o.table_number::text
                  AND (SELECT COUNT(*) FROM "Tables" AS candidate
                       WHERE candidate.table_number = o.table_number::text) = 1;

                UPDATE table_service_sessions AS s
                SET table_id = t.id
                FROM "Tables" AS t
                WHERE s.table_number IS NOT NULL
                  AND s.table_number::text ~ '^[0-9]+$'
                  AND t.table_number = s.table_number::text
                  AND (SELECT COUNT(*) FROM "Tables" AS candidate
                       WHERE candidate.table_number = s.table_number::text) = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_table_service_sessions_table_id",
                table: "table_service_sessions",
                column: "table_id",
                unique: true,
                filter: "\"status\" = 'Open' AND \"table_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_orders_table_id",
                table: "orders",
                column: "table_id");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_tables_table_id",
                table: "orders",
                column: "table_id",
                principalTable: "Tables",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_table_service_sessions_tables_table_id",
                table: "table_service_sessions",
                column: "table_id",
                principalTable: "Tables",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM table_service_sessions WHERE table_number IS NULL)
                        OR EXISTS (SELECT 1 FROM table_bill_payment_operations WHERE table_number IS NULL)
                        OR EXISTS (SELECT 1 FROM orders
                                   WHERE table_number IS NULL
                                     AND (table_id IS NOT NULL OR table_label IS NOT NULL))
                    THEN
                        RAISE EXCEPTION 'Cannot downgrade stable table identity while alphanumeric table records exist';
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "table_number",
                table: "table_bill_payment_operations",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int?),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "table_number",
                table: "table_service_sessions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int?),
                oldType: "integer",
                oldNullable: true);
            migrationBuilder.DropForeignKey(
                name: "fk_orders_tables_table_id",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "fk_table_service_sessions_tables_table_id",
                table: "table_service_sessions");

            migrationBuilder.DropIndex(
                name: "ix_table_service_sessions_table_id",
                table: "table_service_sessions");

            migrationBuilder.DropIndex(
                name: "ix_orders_table_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "table_id",
                table: "table_service_sessions");

            migrationBuilder.DropColumn(
                name: "table_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "table_label",
                table: "orders");
        }
    }
}
