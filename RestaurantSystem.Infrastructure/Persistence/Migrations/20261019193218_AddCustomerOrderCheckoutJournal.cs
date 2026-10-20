using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerOrderCheckoutJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_order_checkout_journals",
                columns: table => new
                {
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    application_fee_minor = table.Column<long>(type: "bigint", nullable: true),
                    provider_account_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    provider_live_mode = table.Column<bool>(type: "boolean", nullable: false),
                    create_idempotency_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    create_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    return_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    maximum_create_retry_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    provider_session_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_order_checkout_journals", x => x.order_id);
                    table.CheckConstraint("ck_customer_order_checkout_journal_shape", "order_id <> '00000000-0000-0000-0000-000000000000'::uuid AND operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND amount_minor > 0 AND currency ~ '^[a-z]{3}$' AND (application_fee_minor IS NULL OR (application_fee_minor > 0 AND application_fee_minor < amount_minor)) AND expires_at > started_at AND maximum_create_retry_at > started_at AND maximum_create_retry_at <= started_at + INTERVAL '23 hours' AND ((lease_id IS NULL AND lease_expires_at IS NULL) OR (lease_id IS NOT NULL AND lease_expires_at IS NOT NULL))");
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_order_checkout_journals_create_idempotency_key",
                table: "customer_order_checkout_journals",
                column: "create_idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_order_checkout_journals_operation_id",
                table: "customer_order_checkout_journals",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_order_checkout_journals_provider_session_id",
                table: "customer_order_checkout_journals",
                column: "provider_session_id",
                unique: true,
                filter: "provider_session_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE customer_order_checkout_journals IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM customer_order_checkout_journals)
                    THEN
                        RAISE EXCEPTION 'Native checkout recovery history must be retained' USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "customer_order_checkout_journals");
        }
    }
}
