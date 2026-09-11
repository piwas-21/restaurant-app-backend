using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTableBillPaymentOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "table_bill_payment_operation_id",
                table: "order_payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "table_bill_payment_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_number = table.Column<int>(type: "integer", nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reference_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    card_last_four_digits = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    card_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    payment_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_table_bill_payment_operations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_payments_table_bill_payment_operation_id",
                table: "order_payments",
                column: "table_bill_payment_operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_bill_payment_operations_operation_id",
                table: "table_bill_payment_operations",
                column: "operation_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_order_payments_tablebillpaymentoperations_table_bill_paymen~",
                table: "order_payments",
                column: "table_bill_payment_operation_id",
                principalTable: "table_bill_payment_operations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_order_payments_tablebillpaymentoperations_table_bill_paymen~",
                table: "order_payments");

            migrationBuilder.DropTable(
                name: "table_bill_payment_operations");

            migrationBuilder.DropIndex(
                name: "ix_order_payments_table_bill_payment_operation_id",
                table: "order_payments");

            migrationBuilder.DropColumn(
                name: "table_bill_payment_operation_id",
                table: "order_payments");
        }
    }
}
