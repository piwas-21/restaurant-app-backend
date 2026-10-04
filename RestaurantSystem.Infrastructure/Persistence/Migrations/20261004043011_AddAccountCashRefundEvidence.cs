using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountCashRefundEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_cash_refund_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    refund_leg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    original_exact_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    original_adjustment_minor = table.Column<long>(type: "bigint", nullable: false),
                    original_due_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    previously_refunded_exact_minor = table.Column<long>(type: "bigint", nullable: false),
                    previously_refunded_cash_minor = table.Column<long>(type: "bigint", nullable: false),
                    exact_refund_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    refund_adjustment_minor = table.Column<long>(type: "bigint", nullable: false),
                    cash_refund_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    retained_exact_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    retained_cash_due_minor = table.Column<long>(type: "bigint", nullable: false),
                    prior_history_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_cash_refund_intents", x => x.id);
                    table.CheckConstraint("ck_account_cash_refund_intent_shape", "original_exact_amount_minor > 0 AND original_due_amount_minor > 0 AND original_adjustment_minor = original_due_amount_minor - original_exact_amount_minor AND original_adjustment_minor BETWEEN -2 AND 2 AND previously_refunded_exact_minor >= 0 AND previously_refunded_cash_minor >= 0 AND exact_refund_amount_minor > 0 AND cash_refund_amount_minor >= 0 AND refund_adjustment_minor = cash_refund_amount_minor - exact_refund_amount_minor AND retained_exact_amount_minor >= 0 AND retained_cash_due_minor >= 0 AND original_exact_amount_minor = previously_refunded_exact_minor + exact_refund_amount_minor + retained_exact_amount_minor AND original_due_amount_minor = previously_refunded_cash_minor + cash_refund_amount_minor + retained_cash_due_minor AND currency ~ '^[A-Z]{3}$' AND prior_history_fingerprint ~ '^[a-f0-9]{64}$' AND ((policy_version = 'chf-cash-5-rappen-v1' AND currency = 'CHF') OR (policy_version = 'exact-v1' AND currency <> 'CHF' AND original_due_amount_minor = original_exact_amount_minor))");
                    table.ForeignKey(
                        name: "fk_account_cash_refund_intents_account_cash_collection_receipt~",
                        column: x => x.collection_receipt_id,
                        principalTable: "account_cash_collection_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_cash_refund_intents_accountpaymentattempts_attempt_~",
                        column: x => x.attempt_id,
                        principalTable: "account_payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_cash_refund_intents_orderamendmentrefundlegs_refund~",
                        column: x => x.refund_leg_id,
                        principalTable: "order_amendment_refund_legs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_account_cash_refund_intents_orderamendmentresolutionoperati~",
                        column: x => x.operation_id,
                        principalTable: "order_amendment_resolution_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_cash_refund_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exact_refund_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    refund_adjustment_minor = table.Column<long>(type: "bigint", nullable: false),
                    cash_returned_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    till_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_cash_refund_evidence", x => x.id);
                    table.CheckConstraint("ck_account_cash_refund_evidence_shape", "exact_refund_amount_minor > 0 AND cash_returned_minor >= 0 AND refund_adjustment_minor = cash_returned_minor - exact_refund_amount_minor AND currency ~ '^[A-Z]{3}$' AND actor_id <> '00000000-0000-0000-0000-000000000000' AND actor_role = 'Admin' AND length(till_reference) BETWEEN 1 AND 80");
                    table.ForeignKey(
                        name: "fk_account_cash_refund_evidence_accountcashrefundintents_inten~",
                        column: x => x.intent_id,
                        principalTable: "account_cash_refund_intents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_cash_refund_evidence_intent_id",
                table: "account_cash_refund_evidence",
                column: "intent_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_cash_refund_intents_attempt_id",
                table: "account_cash_refund_intents",
                column: "attempt_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_cash_refund_intents_collection_receipt_id",
                table: "account_cash_refund_intents",
                column: "collection_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_cash_refund_intents_operation_id",
                table: "account_cash_refund_intents",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_account_cash_refund_intents_refund_leg_id",
                table: "account_cash_refund_intents",
                column: "refund_leg_id",
                unique: true);

            ProtectAccountCashRefundHistory(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RequireNoAccountCashRefundHistory(migrationBuilder);
            migrationBuilder.DropTable(
                name: "account_cash_refund_evidence");

            migrationBuilder.DropTable(
                name: "account_cash_refund_intents");
        }
    }
}
