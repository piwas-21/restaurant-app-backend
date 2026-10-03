using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountCheckoutJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "actor_id",
                table: "account_equal_share_plans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "actor_kind",
                table: "account_equal_share_plans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "account_checkout_journals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_attempt_version = table.Column<int>(type: "integer", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    provider_account_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    provider_live_mode = table.Column<bool>(type: "boolean", nullable: false),
                    create_idempotency_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    create_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    return_base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    maximum_create_retry_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    provider_session_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_intent_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_charge_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    provider_captured_minor = table.Column<long>(type: "bigint", nullable: false),
                    provider_refunded_minor = table.Column<long>(type: "bigint", nullable: false),
                    reconciliation_required = table.Column<bool>(type: "boolean", nullable: false),
                    cancel_requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    next_reconcile_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    webhook_wakeup_pending = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reconcile_failure_count = table.Column<int>(type: "integer", nullable: false),
                    last_failure_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    receipt_credential_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    receipt_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_checkout_journals", x => x.id);
                    table.CheckConstraint("ck_account_checkout_journal_shape", "amount_minor > 0 AND started_attempt_version > 0 AND provider_captured_minor >= 0 AND provider_captured_minor <= amount_minor AND provider_refunded_minor >= 0 AND provider_refunded_minor <= provider_captured_minor AND expires_at > started_at AND maximum_create_retry_at > started_at AND maximum_create_retry_at <= started_at + INTERVAL '23 hours' AND ((lease_id IS NULL AND lease_expires_at IS NULL) OR (lease_id IS NOT NULL AND lease_expires_at IS NOT NULL)) AND currency ~ '^[A-Z]{3}$' AND reconcile_failure_count >= 0 AND ((receipt_credential_hash IS NULL AND receipt_expires_at IS NULL) OR (receipt_credential_hash IS NOT NULL AND receipt_expires_at IS NOT NULL))");
                    table.ForeignKey(
                        name: "fk_account_checkout_journals_accountpaymentattempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "account_payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_checkout_journals_attempt_id",
                table: "account_checkout_journals",
                column: "attempt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_checkout_journals_create_idempotency_key",
                table: "account_checkout_journals",
                column: "create_idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_checkout_journals_next_reconcile_at_lease_expires_at",
                table: "account_checkout_journals",
                columns: new[] { "next_reconcile_at", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_account_checkout_journals_provider_charge_id",
                table: "account_checkout_journals",
                column: "provider_charge_id",
                unique: true,
                filter: "provider_charge_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_account_checkout_journals_provider_intent_id",
                table: "account_checkout_journals",
                column: "provider_intent_id",
                unique: true,
                filter: "provider_intent_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_account_checkout_journals_provider_session_id",
                table: "account_checkout_journals",
                column: "provider_session_id",
                unique: true,
                filter: "provider_session_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_checkout_journals");

            migrationBuilder.DropColumn(
                name: "actor_id",
                table: "account_equal_share_plans");

            migrationBuilder.DropColumn(
                name: "actor_kind",
                table: "account_equal_share_plans");
        }
    }
}
