using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntrancePositionToRestaurantInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "entrance_position_x",
                table: "RestaurantInfo",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "entrance_position_y",
                table: "RestaurantInfo",
                type: "numeric(5,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "entrance_position_x",
                table: "RestaurantInfo");

            migrationBuilder.DropColumn(
                name: "entrance_position_y",
                table: "RestaurantInfo");
        }
    }
}
