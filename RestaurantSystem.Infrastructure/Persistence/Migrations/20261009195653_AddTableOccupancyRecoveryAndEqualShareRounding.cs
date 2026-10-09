using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTableOccupancyRecoveryAndEqualShareRounding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_equal_share_plan_shape",
                table: "account_equal_share_plans");

            migrationBuilder.AddColumn<int>(
                name: "rounding_increment_minor",
                table: "account_equal_share_plans",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "table_occupancy_recovery_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    preview_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    expected_readiness_version = table.Column<int>(type: "integer", nullable: false),
                    outcome_readiness_version = table.Column<int>(type: "integer", nullable: false),
                    outcome_readiness_state = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    expected_session_version = table.Column<int>(type: "integer", nullable: true),
                    expected_account_revision = table.Column<long>(type: "bigint", nullable: true),
                    outcome_session_version = table.Column<int>(type: "integer", nullable: true),
                    outcome_account_revision = table.Column<long>(type: "bigint", nullable: true),
                    visit_released_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_occupancy_recovery_operations", x => x.id);
                    table.CheckConstraint("ck_table_occupancy_recovery_operation_shape", "expected_readiness_version > 0 AND outcome_readiness_version > 0 AND actor_role IN ('Admin', 'Cashier', 'Server') AND outcome_readiness_state = 'NeedsReset' AND length(request_hash) = 64 AND length(preview_fingerprint) = 64 AND length(reason) BETWEEN 1 AND 500 AND ((service_session_id IS NULL AND expected_session_version IS NULL AND expected_account_revision IS NULL AND outcome_session_version IS NULL AND outcome_account_revision IS NULL) OR (service_session_id IS NOT NULL AND expected_session_version > 0 AND expected_account_revision > 0 AND outcome_session_version > 0 AND outcome_account_revision > 0))");
                    table.ForeignKey(
                        name: "FK_table_occupancy_recovery_operations_Tables_table_id",
                        column: x => x.table_id,
                        principalTable: "Tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_table_occupancy_recovery_operations_table_service_sessions_~",
                        column: x => x.service_session_id,
                        principalTable: "table_service_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "table_occupancy_recovery_dispositions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    was_legacy_unassigned = table.Column<bool>(type: "boolean", nullable: false),
                    original_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    original_payment_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    original_total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    original_billing_credit_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    original_total_paid = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    original_remaining_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    was_kitchen_released = table.Column<bool>(type: "boolean", nullable: false),
                    had_routing_history = table.Column<bool>(type: "boolean", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_occupancy_recovery_dispositions", x => x.id);
                    table.CheckConstraint("ck_table_occupancy_recovery_disposition_shape", "((was_legacy_unassigned AND service_session_id IS NULL) OR (NOT was_legacy_unassigned AND service_session_id IS NOT NULL)) AND (kind <> 'ArchivedLegacyOccupancy' OR was_legacy_unassigned) AND (kind <> 'RetainedInPriorVisit' OR NOT was_legacy_unassigned)");
                    table.ForeignKey(
                        name: "FK_table_occupancy_recovery_dispositions_Tables_table_id",
                        column: x => x.table_id,
                        principalTable: "Tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_table_occupancy_recovery_dispositions_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_table_occupancy_recovery_dispositions_table_occupancy_recov~",
                        column: x => x.operation_id,
                        principalTable: "table_occupancy_recovery_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_table_occupancy_recovery_dispositions_table_service_session~",
                        column: x => x.service_session_id,
                        principalTable: "table_service_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_equal_share_plan_shape",
                table: "account_equal_share_plans",
                sql: "total_minor > 0 AND share_count > 0 AND account_revision > 0 AND rounding_increment_minor > 0");

            migrationBuilder.CreateIndex(
                name: "IX_table_occupancy_recovery_dispositions_operation_id_order_id",
                table: "table_occupancy_recovery_dispositions",
                columns: new[] { "operation_id", "order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_table_occupancy_recovery_dispositions_order_id_was_legacy_u~",
                table: "table_occupancy_recovery_dispositions",
                columns: new[] { "order_id", "was_legacy_unassigned" });

            migrationBuilder.CreateIndex(
                name: "IX_table_occupancy_recovery_dispositions_service_session_id",
                table: "table_occupancy_recovery_dispositions",
                column: "service_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_occupancy_recovery_dispositions_table_id_order_id",
                table: "table_occupancy_recovery_dispositions",
                columns: new[] { "table_id", "order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_table_occupancy_recovery_operations_service_session_id",
                table: "table_occupancy_recovery_operations",
                column: "service_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_occupancy_recovery_operations_table_id_id",
                table: "table_occupancy_recovery_operations",
                columns: new[] { "table_id", "id" },
                unique: true);

            migrationBuilder.Sql(ProtectTableOccupancyRecoveryHistory);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RequireUnusedTableOccupancyRecoveryHistory);
            migrationBuilder.DropTable(
                name: "table_occupancy_recovery_dispositions");

            migrationBuilder.DropTable(
                name: "table_occupancy_recovery_operations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_equal_share_plan_shape",
                table: "account_equal_share_plans");

            migrationBuilder.DropColumn(
                name: "rounding_increment_minor",
                table: "account_equal_share_plans");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_equal_share_plan_shape",
                table: "account_equal_share_plans",
                sql: "total_minor > 0 AND share_count > 0 AND account_revision > 0");

            migrationBuilder.Sql("DROP FUNCTION reject_table_occupancy_recovery_mutation()");
        }
    }
}
