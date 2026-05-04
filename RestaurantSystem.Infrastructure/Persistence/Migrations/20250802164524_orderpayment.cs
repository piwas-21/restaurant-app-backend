using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class orderpayment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Order");

            migrationBuilder.RenameColumn(
                name: "PaymentDate",
                table: "Order",
                newName: "FocusedAt");

            migrationBuilder.AddColumn<string>(
                name: "FocusReason",
                table: "Order",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FocusedBy",
                table: "Order",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFocusOrder",
                table: "Order",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Order",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RemainingAmount",
                table: "Order",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalPaid",
                table: "Order",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "OrderPayment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CardLastFourDigits = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    CardType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PaymentGateway = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PaymentNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsRefunded = table.Column<bool>(type: "boolean", nullable: false),
                    RefundedAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    RefundDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPayment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderPayment_Order_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Order",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Order_IsFocusOrder",
                table: "Order",
                column: "IsFocusOrder");

            migrationBuilder.CreateIndex(
                name: "IX_Order_IsFocusOrder_Priority",
                table: "Order",
                columns: new[] { "IsFocusOrder", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayment_OrderId",
                table: "OrderPayment",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayment_PaymentDate",
                table: "OrderPayment",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayment_TransactionId",
                table: "OrderPayment",
                column: "TransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderPayment");

            migrationBuilder.DropIndex(
                name: "IX_Order_IsFocusOrder",
                table: "Order");

            migrationBuilder.DropIndex(
                name: "IX_Order_IsFocusOrder_Priority",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "FocusReason",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "FocusedBy",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "IsFocusOrder",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "RemainingAmount",
                table: "Order");

            migrationBuilder.DropColumn(
                name: "TotalPaid",
                table: "Order");

            migrationBuilder.RenameColumn(
                name: "FocusedAt",
                table: "Order",
                newName: "PaymentDate");

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Order",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }
    }
}
