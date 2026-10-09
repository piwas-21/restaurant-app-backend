using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTableSessionLifecycleAndTenderTips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_table_service_sessions_table_id",
                table: "table_service_sessions");

            migrationBuilder.DropIndex(
                name: "IX_table_service_sessions_table_number",
                table: "table_service_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_payment_attempt_shape",
                table: "account_payment_attempts");

            migrationBuilder.AddColumn<DateTime>(
                name: "released_at",
                table: "table_service_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "released_by",
                table: "table_service_sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "ix_table_service_sessions_table_id",
                table: "table_service_sessions",
                column: "table_id",
                unique: true,
                filter: "\"status\" = 'Open' AND \"released_at\" IS NULL AND \"table_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_table_service_sessions_table_number",
                table: "table_service_sessions",
                column: "table_number",
                unique: true,
                filter: "\"status\" = 'Open' AND \"released_at\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_payment_attempt_shape",
                table: "account_payment_attempts",
                sql: "amount_minor > 0 AND tip_minor >= 0 AND expected_account_revision > 0 AND version > 0 AND (equal_share_ordinal IS NULL OR equal_share_ordinal > 0) AND ((mode IN ('Equal', 'CustomAmount') AND equal_share_plan_id IS NOT NULL AND equal_share_ordinal IS NOT NULL) OR (mode NOT IN ('Equal', 'CustomAmount') AND equal_share_plan_id IS NULL AND equal_share_ordinal IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM table_service_sessions
                        WHERE released_at IS NOT NULL OR released_by IS NOT NULL)
                       OR EXISTS (
                        SELECT 1 FROM table_service_sessions
                        WHERE status = 'Open' AND table_number IS NOT NULL
                        GROUP BY table_number HAVING COUNT(*) > 1)
                       OR EXISTS (
                        SELECT 1 FROM table_service_sessions
                        WHERE status = 'Open' AND table_id IS NOT NULL
                        GROUP BY table_id HAVING COUNT(*) > 1)
                       OR EXISTS (
                        SELECT 1 FROM table_bill_payment_operations WHERE tip_minor <> 0)
                       OR EXISTS (
                        SELECT 1 FROM account_payment_attempts
                        WHERE tip_minor <> 0 OR mode = 'CustomAmount')
                       OR EXISTS (
                        SELECT 1 FROM account_equal_share_plans WHERE custom_amounts_json IS NOT NULL)
                    THEN
                        RAISE EXCEPTION 'Table session lifecycle, tender-tip, or custom split history must be retained; use a compatible application build and roll forward. Roll back only an unused schema after supported settlement and cleanup.'
                            USING ERRCODE = '23514';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropIndex(
                name: "ix_table_service_sessions_table_id",
                table: "table_service_sessions");

            migrationBuilder.DropIndex(
                name: "IX_table_service_sessions_table_number",
                table: "table_service_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_payment_attempt_shape",
                table: "account_payment_attempts");

            migrationBuilder.DropColumn(
                name: "released_at",
                table: "table_service_sessions");

            migrationBuilder.DropColumn(
                name: "released_by",
                table: "table_service_sessions");

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

            migrationBuilder.CreateIndex(
                name: "ix_table_service_sessions_table_id",
                table: "table_service_sessions",
                column: "table_id",
                unique: true,
                filter: "\"status\" = 'Open' AND \"table_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_table_service_sessions_table_number",
                table: "table_service_sessions",
                column: "table_number",
                unique: true,
                filter: "\"status\" = 'Open'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_payment_attempt_shape",
                table: "account_payment_attempts",
                sql: "amount_minor > 0 AND expected_account_revision > 0 AND version > 0 AND (equal_share_ordinal IS NULL OR equal_share_ordinal > 0) AND ((mode = 'Equal' AND equal_share_plan_id IS NOT NULL AND equal_share_ordinal IS NOT NULL) OR (mode <> 'Equal' AND equal_share_plan_id IS NULL AND equal_share_ordinal IS NULL))");
        }
    }
}
