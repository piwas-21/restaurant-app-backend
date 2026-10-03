using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTableVisitReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "readiness_state",
                table: "Tables",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "NeedsReset");

            migrationBuilder.AddColumn<int>(
                name: "readiness_version",
                table: "Tables",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "table_ready_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expected_readiness_version = table.Column<int>(type: "integer", nullable: false),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    outcome_error_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    outcome_state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    outcome_readiness_version = table.Column<int>(type: "integer", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_ready_operations", x => x.id);
                    table.CheckConstraint("ck_table_ready_operation_shape", "expected_readiness_version > 0 AND outcome_readiness_version > 0 AND actor_role IN ('Admin', 'Cashier', 'Server') AND outcome_state IN ('NeedsReset', 'ReadyForGuests') AND ((succeeded AND outcome_error_code IS NULL AND outcome_state = 'ReadyForGuests' AND outcome_readiness_version::bigint = expected_readiness_version::bigint + 1) OR (NOT succeeded AND outcome_error_code IS NOT NULL AND length(outcome_error_code) > 0))");
                    table.ForeignKey(
                        name: "fk_table_ready_operations_tables_table_id",
                        column: x => x.table_id,
                        principalTable: "Tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_table_readiness_shape",
                table: "Tables",
                sql: "readiness_version > 0 AND readiness_state IN ('NeedsReset', 'ReadyForGuests')");

            migrationBuilder.CreateIndex(
                name: "IX_table_ready_operations_table_id",
                table: "table_ready_operations",
                column: "table_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_ready_operations_table_id_operation_id",
                table: "table_ready_operations",
                columns: new[] { "table_id", "operation_id" },
                unique: true);
            migrationBuilder.Sql(ProtectReadyOperationHistory);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RequireUnusedReadiness);
            migrationBuilder.DropTable(
                name: "table_ready_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_table_readiness_shape",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "readiness_state",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "readiness_version",
                table: "Tables");
        }
    }
}
