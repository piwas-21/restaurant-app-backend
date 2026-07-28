using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderQuickActionToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "quick_action_token",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_quick_action_token",
                table: "orders",
                column: "quick_action_token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_quick_action_token",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "quick_action_token",
                table: "orders");
        }
    }
}
