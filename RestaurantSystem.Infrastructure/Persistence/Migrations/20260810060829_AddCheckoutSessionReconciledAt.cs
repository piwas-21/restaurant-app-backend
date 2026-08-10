using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutSessionReconciledAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_order_checkout_sessions_status_expires_at",
                table: "order_checkout_sessions");

            migrationBuilder.AddColumn<DateTime>(
                name: "reconciled_at",
                table: "order_checkout_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_checkout_sessions_status_created_at",
                table: "order_checkout_sessions",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_order_checkout_sessions_status_created_at",
                table: "order_checkout_sessions");

            migrationBuilder.DropColumn(
                name: "reconciled_at",
                table: "order_checkout_sessions");

            migrationBuilder.CreateIndex(
                name: "IX_order_checkout_sessions_status_expires_at",
                table: "order_checkout_sessions",
                columns: new[] { "status", "expires_at" });
        }
    }
}
