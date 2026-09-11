using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTableServiceSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "table_bill_payment_operations",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "expected_version",
                table: "table_bill_payment_operations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "service_session_id",
                table: "table_bill_payment_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "service_session_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "table_service_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    table_number = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_service_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_table_bill_payment_operations_service_session_id",
                table: "table_bill_payment_operations",
                column: "service_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_service_session_id",
                table: "orders",
                column: "service_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_service_sessions_opened_at",
                table: "table_service_sessions",
                column: "opened_at");

            migrationBuilder.CreateIndex(
                name: "IX_table_service_sessions_status",
                table: "table_service_sessions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_table_service_sessions_table_number",
                table: "table_service_sessions",
                column: "table_number",
                unique: true,
                filter: "\"status\" = 'Open'");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_tableservicesessions_service_session_id",
                table: "orders",
                column: "service_session_id",
                principalTable: "table_service_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_table_bill_payment_operations_tableservicesessions_service_~",
                table: "table_bill_payment_operations",
                column: "service_session_id",
                principalTable: "table_service_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_orders_tableservicesessions_service_session_id",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "fk_table_bill_payment_operations_tableservicesessions_service_~",
                table: "table_bill_payment_operations");

            migrationBuilder.DropTable(
                name: "table_service_sessions");

            migrationBuilder.DropIndex(
                name: "ix_table_bill_payment_operations_service_session_id",
                table: "table_bill_payment_operations");

            migrationBuilder.DropIndex(
                name: "ix_orders_service_session_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "table_bill_payment_operations");

            migrationBuilder.DropColumn(
                name: "expected_version",
                table: "table_bill_payment_operations");

            migrationBuilder.DropColumn(
                name: "service_session_id",
                table: "table_bill_payment_operations");

            migrationBuilder.DropColumn(
                name: "service_session_id",
                table: "orders");
        }
    }
}
