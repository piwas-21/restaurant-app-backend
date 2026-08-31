using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantLandingContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "landing_background_mode",
                table: "RestaurantInfo",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Default");

            // A tenant that uploaded an interior photo before this column existed was promised
            // that photo on the landing page (backend #445 + frontend #628 shipped exactly that
            // section). The new mode column defaults to `Default`, which would silently HIDE the
            // photo the founder can already see — so tenants with an upload in hand read
            // `Custom` from day one. Everyone else keeps the platform artwork.
            migrationBuilder.Sql(
                "UPDATE \"RestaurantInfo\" SET \"landing_background_mode\" = 'Custom' " +
                "WHERE \"interior_image_url\" IS NOT NULL AND \"interior_image_url\" <> '';");

            migrationBuilder.CreateTable(
                name: "RestaurantLandingContents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    restaurant_info_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    hero_eyebrow = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    welcome_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    welcome_body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    story_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    story_body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_restaurant_landing_contents", x => x.id);
                    table.ForeignKey(
                        name: "fk_restaurant_landing_contents_restaurant_info_restaurant_info~",
                        column: x => x.restaurant_info_id,
                        principalTable: "RestaurantInfo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantLandingContents_restaurant_info_id_language_code",
                table: "RestaurantLandingContents",
                columns: new[] { "restaurant_info_id", "language_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestaurantLandingContents");

            migrationBuilder.DropColumn(
                name: "landing_background_mode",
                table: "RestaurantInfo");
        }
    }
}
