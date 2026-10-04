using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261004031747_AddAccountCashCollectionReceipts")]
    public partial class AddAccountCashCollectionReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_cash_collection_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    exact_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    adjustment_minor = table.Column<long>(type: "bigint", nullable: false),
                    due_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    received_minor = table.Column<long>(type: "bigint", nullable: false),
                    change_minor = table.Column<long>(type: "bigint", nullable: false),
                    expected_account_revision = table.Column<long>(type: "bigint", nullable: false),
                    expected_version = table.Column<int>(type: "integer", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_cash_collection_receipt", x => x.id);
                    table.CheckConstraint("ck_account_cash_collection_receipt_shape", "payment_method = 'Cash' AND exact_amount_minor > 0 AND due_amount_minor > 0 AND adjustment_minor = due_amount_minor - exact_amount_minor AND adjustment_minor BETWEEN -2 AND 2 AND received_minor >= due_amount_minor AND change_minor = received_minor - due_amount_minor AND expected_account_revision > 0 AND expected_version > 0 AND currency ~ '^[A-Z]{3}$' AND actor_id <> '00000000-0000-0000-0000-000000000000' AND actor_kind = 'Staff' AND actor_role IN ('Admin','Cashier','Server') AND request_hash ~ '^[a-f0-9]{64}$' AND ((policy_version = 'chf-cash-5-rappen-v1' AND currency = 'CHF' AND due_amount_minor % 5 = 0) OR (policy_version = 'exact-v1' AND currency <> 'CHF' AND due_amount_minor = exact_amount_minor))");
                    table.ForeignKey(
                        name: "fk_account_cash_collection_receipt_accountpaymentattempts_atte~",
                        column: x => x.attempt_id,
                        principalTable: "account_payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_cash_collection_receipt_attempt_id",
                table: "account_cash_collection_receipts",
                column: "attempt_id",
                unique: true);

            ProtectAccountCashCollectionReceiptHistory(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RequireNoAccountCashCollectionReceiptHistory(migrationBuilder);
            migrationBuilder.DropTable(
                name: "account_cash_collection_receipts");
        }
    }
}
