using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Drops <c>BasketItems.excluded_ingredients</c> (frontend issue #170).
    ///
    /// DESTRUCTIVE, and deliberately so. The field was never populated: no write site exists in
    /// any of the three repos, and removal has always flowed through
    /// <c>IngredientQuantitiesJson</c> instead (quantity 0), which is what the order and kitchen
    /// pipelines actually read. Measured on the live production database before writing this
    /// migration rather than inferred from the code: of 19 <c>BasketItems</c> rows, 4 held a
    /// non-null value and <b>0</b> held a non-empty array — i.e. the column has only ever
    /// contained <c>[]</c>.
    ///
    /// <c>Down</c> re-adds the column, not its contents. That is a faithful inverse here
    /// precisely because there are no contents to lose. Baskets are also short-lived
    /// (BasketCleanup background service), so even the empty arrays are transient.
    /// </summary>
    public partial class DropVestigialBasketExcludedIngredients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "excluded_ingredients",
                table: "BasketItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "excluded_ingredients",
                table: "BasketItems",
                type: "jsonb",
                nullable: true);
        }
    }
}
