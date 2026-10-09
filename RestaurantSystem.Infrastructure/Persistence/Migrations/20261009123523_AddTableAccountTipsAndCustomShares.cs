using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTableAccountTipsAndCustomShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_payment_attempt_shape",
                table: "account_payment_attempts");

            migrationBuilder.AddColumn<long>(
                name: "tip_minor",
                table: "table_bill_payment_operations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "account_payment_attempts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AddColumn<long>(
                name: "tip_minor",
                table: "account_payment_attempts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "custom_amounts_json",
                table: "account_equal_share_plans",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_payment_attempt_shape",
                table: "account_payment_attempts",
                sql: "amount_minor > 0 AND tip_minor >= 0 AND expected_account_revision > 0 AND version > 0 AND (equal_share_ordinal IS NULL OR equal_share_ordinal > 0) AND ((mode IN ('Equal', 'CustomAmount') AND equal_share_plan_id IS NOT NULL AND equal_share_ordinal IS NOT NULL) OR (mode NOT IN ('Equal', 'CustomAmount') AND equal_share_plan_id IS NULL AND equal_share_ordinal IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_payment_attempt_shape",
                table: "account_payment_attempts");

            migrationBuilder.DropColumn(
                name: "tip_minor",
                table: "table_bill_payment_operations");

            migrationBuilder.DropColumn(
                name: "tip_minor",
                table: "account_payment_attempts");

            migrationBuilder.DropColumn(
                name: "custom_amounts_json",
                table: "account_equal_share_plans");

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "account_payment_attempts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_payment_attempt_shape",
                table: "account_payment_attempts",
                sql: "amount_minor > 0 AND expected_account_revision > 0 AND version > 0 AND (equal_share_ordinal IS NULL OR equal_share_ordinal > 0) AND ((mode = 'Equal' AND equal_share_plan_id IS NOT NULL AND equal_share_ordinal IS NOT NULL) OR (mode <> 'Equal' AND equal_share_plan_id IS NULL AND equal_share_ordinal IS NULL))");
        }
    }
}
