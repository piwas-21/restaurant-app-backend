using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeTableCheckoutScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "native_operation_id",
                table: "account_checkout_journals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "native_tenant_id",
                table: "account_checkout_journals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_checkout_journals_native_operation_id",
                table: "account_checkout_journals",
                column: "native_operation_id",
                unique: true,
                filter: "native_operation_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_checkout_native_scope",
                table: "account_checkout_journals",
                sql: "(native_operation_id IS NULL AND native_tenant_id IS NULL) OR (native_operation_id IS NOT NULL AND native_tenant_id IS NOT NULL AND native_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND native_tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid)");

            migrationBuilder.AddForeignKey(
                name: "FK_account_checkout_journals_customer_table_payment_operations~",
                table: "account_checkout_journals",
                column: "native_operation_id",
                principalTable: "customer_table_payment_operations",
                principalColumn: "operation_id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE customer_table_payment_operations, account_checkout_journals IN ACCESS EXCLUSIVE MODE NOWAIT;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM account_checkout_journals WHERE native_operation_id IS NOT NULL) THEN
                        RAISE EXCEPTION 'Native provider requests must be retained' USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_account_checkout_journals_customer_table_payment_operations~",
                table: "account_checkout_journals");

            migrationBuilder.DropIndex(
                name: "IX_account_checkout_journals_native_operation_id",
                table: "account_checkout_journals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_checkout_native_scope",
                table: "account_checkout_journals");

            migrationBuilder.DropColumn(
                name: "native_operation_id",
                table: "account_checkout_journals");

            migrationBuilder.DropColumn(
                name: "native_tenant_id",
                table: "account_checkout_journals");
        }
    }
}
