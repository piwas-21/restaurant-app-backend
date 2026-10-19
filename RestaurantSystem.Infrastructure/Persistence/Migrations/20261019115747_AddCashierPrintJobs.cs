using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashierPrintJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cashier_print_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_automatic = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    claimed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cashier_print_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_cashier_print_jobs_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cashier_print_jobs_tableservicesessions_service_session_id",
                        column: x => x.service_session_id,
                        principalTable: "table_service_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cashier_print_jobs_order_id",
                table: "cashier_print_jobs",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_cashier_print_jobs_service_session_id",
                table: "cashier_print_jobs",
                column: "service_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_cashier_print_jobs_status_created_at",
                table: "cashier_print_jobs",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE cashier_print_jobs IN ACCESS EXCLUSIVE MODE;
                DO $guard$
                BEGIN
                    IF EXISTS (SELECT 1 FROM cashier_print_jobs) THEN
                        RAISE EXCEPTION 'Cashier print request history must be retained' USING ERRCODE = '23514';
                    END IF;
                END;
                $guard$;
                """);
            migrationBuilder.DropTable(
                name: "cashier_print_jobs");
        }
    }
}
