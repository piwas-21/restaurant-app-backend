using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountPaymentLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "resolved_account_payment_attempt_id",
                table: "table_service_payment_handoffs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "account_equal_share_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    service_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_revision = table.Column<long>(type: "bigint", nullable: false),
                    total_minor = table.Column<long>(type: "bigint", nullable: false),
                    share_count = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope_json = table.Column<string>(type: "jsonb", nullable: false),
                    invalidated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    supersedes_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_equal_share_plans", x => x.id);
                    table.CheckConstraint("ck_account_equal_share_plan_shape", "total_minor > 0 AND share_count > 0 AND account_revision > 0");
                    table.ForeignKey(
                        name: "fk_account_equal_share_plans_tableservicesessions_service_sess~",
                        column: x => x.service_session_id,
                        principalTable: "table_service_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_payment_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    service_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    expected_account_revision = table.Column<long>(type: "bigint", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    quote_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reserved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reservation_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    provider_session_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_charge_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_account_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    equal_share_plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    equal_share_ordinal = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_payment_attempts", x => x.id);
                    table.CheckConstraint("ck_account_payment_attempt_shape", "amount_minor > 0 AND expected_account_revision > 0 AND version > 0 AND (equal_share_ordinal IS NULL OR equal_share_ordinal > 0) AND ((mode = 'Equal' AND equal_share_plan_id IS NOT NULL AND equal_share_ordinal IS NOT NULL) OR (mode <> 'Equal' AND equal_share_plan_id IS NULL AND equal_share_ordinal IS NULL))");
                    table.ForeignKey(
                        name: "fk_account_payment_attempts_account_equal_share_plans_equal_sh~",
                        column: x => x.equal_share_plan_id,
                        principalTable: "account_equal_share_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_payment_attempts_tableservicesessions_service_sessi~",
                        column: x => x.service_session_id,
                        principalTable: "table_service_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_payment_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_ordinal = table.Column<int>(type: "integer", nullable: false),
                    unit_count = table.Column<int>(type: "integer", nullable: false),
                    minor_per_unit = table.Column<long>(type: "bigint", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_payment_allocations", x => x.id);
                    table.CheckConstraint("ck_account_payment_allocation_range", "start_ordinal > 0 AND unit_count > 0 AND minor_per_unit > 0 AND start_ordinal::bigint + unit_count <= 2147483648 AND (order_item_id IS NOT NULL OR (start_ordinal = 1 AND unit_count = 1)) AND amount_minor = minor_per_unit * unit_count");
                    table.ForeignKey(
                        name: "fk_account_payment_allocations_accountpaymentattempts_attempt_~",
                        column: x => x.attempt_id,
                        principalTable: "account_payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_payment_allocations_orderitems_order_item_id",
                        column: x => x.order_item_id,
                        principalTable: "OrderItems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_payment_allocations_orderpayments_order_payment_id",
                        column: x => x.order_payment_id,
                        principalTable: "order_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_payment_allocations_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_table_service_payment_handoffs_resolved_account_payment_att~",
                table: "table_service_payment_handoffs",
                column: "resolved_account_payment_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_account_equal_share_plans_operation_id",
                table: "account_equal_share_plans",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_equal_share_plans_service_session_id",
                table: "account_equal_share_plans",
                column: "service_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_payment_allocations_attempt_id",
                table: "account_payment_allocations",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_account_payment_allocations_order_id_order_item_id_start_or~",
                table: "account_payment_allocations",
                columns: new[] { "order_id", "order_item_id", "start_ordinal" });

            migrationBuilder.CreateIndex(
                name: "ix_account_payment_allocations_order_item_id",
                table: "account_payment_allocations",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_payment_allocations_order_payment_id",
                table: "account_payment_allocations",
                column: "order_payment_id");

            migrationBuilder.CreateIndex(
                name: "IX_account_payment_attempts_equal_share_plan_id_equal_share_or~",
                table: "account_payment_attempts",
                columns: new[] { "equal_share_plan_id", "equal_share_ordinal" });

            migrationBuilder.CreateIndex(
                name: "IX_account_payment_attempts_operation_id",
                table: "account_payment_attempts",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_payment_attempts_provider_charge_id",
                table: "account_payment_attempts",
                column: "provider_charge_id",
                unique: true,
                filter: "provider_charge_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_account_payment_attempts_provider_session_id",
                table: "account_payment_attempts",
                column: "provider_session_id",
                unique: true,
                filter: "provider_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_account_payment_attempts_service_session_id_state",
                table: "account_payment_attempts",
                columns: new[] { "service_session_id", "state" });

            migrationBuilder.AddForeignKey(
                name: "FK_table_service_payment_handoffs_account_payment_attempts_res~",
                table: "table_service_payment_handoffs",
                column: "resolved_account_payment_attempt_id",
                principalTable: "account_payment_attempts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_table_service_payment_handoffs_account_payment_attempts_res~",
                table: "table_service_payment_handoffs");

            migrationBuilder.DropTable(
                name: "account_payment_allocations");

            migrationBuilder.DropTable(
                name: "account_payment_attempts");

            migrationBuilder.DropTable(
                name: "account_equal_share_plans");

            migrationBuilder.DropIndex(
                name: "IX_table_service_payment_handoffs_resolved_account_payment_att~",
                table: "table_service_payment_handoffs");

            migrationBuilder.DropColumn(
                name: "resolved_account_payment_attempt_id",
                table: "table_service_payment_handoffs");
        }
    }
}
