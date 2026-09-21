using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderRoutingRequirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_required",
                table: "OrderRoutingStates",
                type: "boolean",
                nullable: false,
                // Existing routes predate the distinction and were all treated as delivery work.
                // Preserve that safety posture; newly created cashier routes explicitly set false.
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_required",
                table: "OrderRoutingStates");
        }
    }
}
