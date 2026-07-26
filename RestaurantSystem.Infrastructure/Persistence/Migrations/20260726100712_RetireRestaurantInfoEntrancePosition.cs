using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantSystem.Infrastructure.Persistence.Support;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Retires <c>RestaurantInfo.entrance_position_x/y</c> (FLOOR-PLAN-REVAMP §6
    /// step 4, slice S10). The entrance moved into the plan document as an
    /// <c>entrance</c> item when S4 landed; these columns stayed on as a read
    /// fallback for one release and are now dead.
    ///
    /// **The drop carries the data first.** The columns held percentages of a
    /// virtual canvas, and an admin may have positioned the marker before the
    /// revamp — so a stored pair is converted to metres against the default
    /// plan's real dimensions and inserted as an entrance item, unless the plan
    /// already has one (the seeded plan does). Dropping without that step would
    /// discard a setting nobody could get back.
    /// </summary>
    public partial class RetireRestaurantInfoEntrancePosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(FloorPlanMigrationSql.CarryEntranceToPlanTemplate);

            migrationBuilder.DropColumn(
                name: "entrance_position_x",
                table: "RestaurantInfo");

            migrationBuilder.DropColumn(
                name: "entrance_position_y",
                table: "RestaurantInfo");
        }

        /// <summary>
        /// Restores the columns, empty. The carried-over entrance item is
        /// deliberately <b>not</b> removed and not read back: the plan document
        /// is the source of truth now, and a down-migration that deleted an item
        /// an admin may since have moved would be a worse outcome than two
        /// nullable columns nothing reads.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
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
    }
}
