using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaidAmendmentFinancialResolutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_amendment_resolution_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    amendment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    expected_order_version = table.Column<int>(type: "integer", nullable: false),
                    expected_account_revision = table.Column<long>(type: "bigint", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    credit_minor = table.Column<long>(type: "bigint", nullable: false),
                    refund_minor = table.Column<long>(type: "bigint", nullable: false),
                    unpaid_waived_minor = table.Column<long>(type: "bigint", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_amendment_resolution_operations", x => x.id);
                    table.CheckConstraint("ck_amendment_resolution_operation_amounts", "credit_minor > 0 AND refund_minor >= 0 AND unpaid_waived_minor >= 0 AND refund_minor + unpaid_waived_minor = credit_minor");
                    table.ForeignKey(
                        name: "FK_order_amendment_resolution_operations_order_amendments_amen~",
                        column: x => x.amendment_id,
                        principalTable: "order_amendments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_resolution_operations_orders_source_order_id",
                        column: x => x.source_order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_resolution_operations_table_service_session~",
                        column: x => x.service_session_id,
                        principalTable: "table_service_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_amendment_refund_legs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    custody = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    frozen_scopes_json = table.Column<string>(type: "jsonb", nullable: false),
                    provider_account_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_live_mode = table.Column<bool>(type: "boolean", nullable: true),
                    provider_charge_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_intent_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    manual_till_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_amendment_refund_legs", x => x.id);
                    table.CheckConstraint("ck_amendment_refund_leg_amount", "amount_minor > 0");
                    table.ForeignKey(
                        name: "FK_order_amendment_refund_legs_account_payment_attempts_accoun~",
                        column: x => x.account_payment_attempt_id,
                        principalTable: "account_payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_refund_legs_order_payments_source_payment_id",
                        column: x => x.source_payment_id,
                        principalTable: "order_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_order_amendment_refund_legs_orderamendmentresolutionoperati~",
                        column: x => x.operation_id,
                        principalTable: "order_amendment_resolution_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_payment_allocation_reversals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_leg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_ordinal = table.Column<int>(type: "integer", nullable: false),
                    unit_count = table.Column<int>(type: "integer", nullable: false),
                    minor_per_unit = table.Column<long>(type: "bigint", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_payment_allocation_reversals", x => x.id);
                    table.CheckConstraint("ck_account_payment_allocation_reversal_range", "start_ordinal > 0 AND unit_count > 0 AND minor_per_unit > 0 AND start_ordinal::bigint + unit_count <= 2147483648 AND (order_item_id IS NOT NULL OR (start_ordinal = 1 AND unit_count = 1)) AND amount_minor = minor_per_unit * unit_count");
                    table.ForeignKey(
                        name: "FK_account_payment_allocation_reversals_OrderItems_order_item_~",
                        column: x => x.order_item_id,
                        principalTable: "OrderItems",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_payment_allocation_reversals_account_payment_alloca~",
                        column: x => x.allocation_id,
                        principalTable: "account_payment_allocations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_payment_allocation_reversals_order_amendment_refund~",
                        column: x => x.refund_leg_id,
                        principalTable: "order_amendment_refund_legs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_payment_allocation_reversals_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_amendment_refund_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    refund_leg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_amendment_refund_attempts", x => x.id);
                    table.CheckConstraint("ck_amendment_refund_attempt_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "fk_order_amendment_refund_attempts_orderamendmentrefundlegs_or~",
                        column: x => x.refund_leg_id,
                        principalTable: "order_amendment_refund_legs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_amendment_refund_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    refund_leg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refund_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    till_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    provider_refund_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_refund_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    provider_charge_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_intent_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_account_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_live_mode = table.Column<bool>(type: "boolean", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_amendment_refund_evidence", x => x.id);
                    table.CheckConstraint("ck_amendment_refund_evidence_amount", "amount_minor >= 0 AND sequence > 0");
                    table.ForeignKey(
                        name: "FK_order_amendment_refund_evidence_order_amendment_refund_atte~",
                        column: x => x.refund_attempt_id,
                        principalTable: "order_amendment_refund_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_refund_evidence_order_amendment_refund_legs~",
                        column: x => x.refund_leg_id,
                        principalTable: "order_amendment_refund_legs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_payment_allocation_reversals_allocation_id_start_or~",
                table: "account_payment_allocation_reversals",
                columns: new[] { "allocation_id", "start_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_payment_allocation_reversals_order_id",
                table: "account_payment_allocation_reversals",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_account_payment_allocation_reversals_order_item_id",
                table: "account_payment_allocation_reversals",
                column: "order_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_account_payment_allocation_reversals_refund_leg_id",
                table: "account_payment_allocation_reversals",
                column: "refund_leg_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_refund_attempts_idempotency_key",
                table: "order_amendment_refund_attempts",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_refund_attempts_refund_leg_id_sequence",
                table: "order_amendment_refund_attempts",
                columns: new[] { "refund_leg_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_refund_evidence_provider_refund_id",
                table: "order_amendment_refund_evidence",
                column: "provider_refund_id",
                unique: true,
                filter: "provider_refund_id IS NOT NULL AND state = 'Succeeded'");

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_refund_evidence_refund_attempt_id",
                table: "order_amendment_refund_evidence",
                column: "refund_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_refund_evidence_refund_leg_id_sequence",
                table: "order_amendment_refund_evidence",
                columns: new[] { "refund_leg_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_refund_legs_account_payment_attempt_id",
                table: "order_amendment_refund_legs",
                column: "account_payment_attempt_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_refund_legs_operation_id_source_payment_id",
                table: "order_amendment_refund_legs",
                columns: new[] { "operation_id", "source_payment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_refund_legs_provider_account_id_provider_ch~",
                table: "order_amendment_refund_legs",
                columns: new[] { "provider_account_id", "provider_charge_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_refund_legs_source_payment_id",
                table: "order_amendment_refund_legs",
                column: "source_payment_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_resolution_operations_actor_user_id_client_~",
                table: "order_amendment_resolution_operations",
                columns: new[] { "actor_user_id", "client_operation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_resolution_operations_amendment_id",
                table: "order_amendment_resolution_operations",
                column: "amendment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_resolution_operations_service_session_id_st~",
                table: "order_amendment_resolution_operations",
                columns: new[] { "service_session_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_resolution_operations_source_order_id",
                table: "order_amendment_resolution_operations",
                column: "source_order_id");

            ProtectPaidFinancialHistory(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RequireNoPaidFinancialHistory(migrationBuilder);
            migrationBuilder.DropTable(
                name: "account_payment_allocation_reversals");

            migrationBuilder.DropTable(
                name: "order_amendment_refund_evidence");

            migrationBuilder.DropTable(
                name: "order_amendment_refund_attempts");

            migrationBuilder.DropTable(
                name: "order_amendment_refund_legs");

            migrationBuilder.DropTable(
                name: "order_amendment_resolution_operations");
        }
    }
}
