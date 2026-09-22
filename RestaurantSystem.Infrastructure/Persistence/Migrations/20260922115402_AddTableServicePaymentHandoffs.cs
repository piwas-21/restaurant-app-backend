using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTableServicePaymentHandoffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "table_service_payment_handoffs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    service_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_version = table.Column<int>(type: "integer", nullable: false),
                    requested_amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    requested_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_payment_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cancellation_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_expected_version = table.Column<int>(type: "integer", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_service_payment_handoffs", x => x.id);
                    table.ForeignKey(
                        name: "fk_table_service_payment_handoffs_tableservicesessions_service~",
                        column: x => x.service_session_id,
                        principalTable: "table_service_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_table_service_payment_handoffs_cancellation_operation_id",
                table: "table_service_payment_handoffs",
                column: "cancellation_operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_table_service_payment_handoffs_operation_id",
                table: "table_service_payment_handoffs",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_table_service_payment_handoffs_status_requested_at",
                table: "table_service_payment_handoffs",
                columns: new[] { "status", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_table_service_payment_handoffs_service_session_id",
                table: "table_service_payment_handoffs",
                column: "service_session_id",
                unique: true,
                filter: "\"status\" = 'Requested'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "table_service_payment_handoffs");
        }
    }
}
