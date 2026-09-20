using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderConfirmationFlowAndGuestStatusToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "guest_status_token",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "confirmation_flow",
                table: "order_type_configurations",
                type: "text",
                nullable: false,
                defaultValue: "direct");

            migrationBuilder.AddColumn<int>(
                name: "review_window_minutes",
                table: "order_type_configurations",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            // Every existing tenant keeps today's behaviour (direct confirmation, no review
            // window) until they opt into the acknowledge flow in settings — the column defaults
            // above already say so; this backfill is belt-and-braces. Purely additive.
            migrationBuilder.Sql(
                """
                UPDATE order_type_configurations
                SET confirmation_flow = 'direct',
                    review_window_minutes = 2
                WHERE confirmation_flow IS DISTINCT FROM 'acknowledge'
                   OR review_window_minutes < 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "guest_status_token",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "confirmation_flow",
                table: "order_type_configurations");

            migrationBuilder.DropColumn(
                name: "review_window_minutes",
                table: "order_type_configurations");
        }
    }
}
