using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RestaurantInfo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    website = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_restaurant_info", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantPhoneNumbers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    restaurant_info_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    whats_app_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValueSql: "true"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_restaurant_phone_numbers", x => x.id);
                    table.ForeignKey(
                        name: "fk_restaurant_phone_numbers_restaurant_info_restaurant_info_id",
                        column: x => x.restaurant_info_id,
                        principalTable: "RestaurantInfo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_restaurant_phone_numbers_restaurant_info_id",
                table: "RestaurantPhoneNumbers",
                column: "restaurant_info_id");

            // Seed the singleton row from the values that previously lived in
            // i18n (rumi_address_*, rumi_phone_number). Stable IDs so the
            // singleton invariant holds across re-runs and so the phone FK
            // can be filled in deterministically. Address values from
            // frontend/src/locales/en.json at the time of this migration.
            //
            // MIGRATION_SEED: real production values inlined here. IOptions<T>
            // and other DI-resolved settings are unavailable inside an EF
            // migration — this is the documented exception to the
            // "no hardcoded emails / addresses" rule (CLAUDE.md backend §5).
            var restaurantInfoId = new Guid("00000000-0000-0000-0000-000000000001");
            var phoneId = new Guid("00000000-0000-0000-0000-000000000002");
            migrationBuilder.InsertData(
                table: "RestaurantInfo",
                columns: new[]
                {
                    "id", "name", "address_line1", "address_line2", "city", "postal_code", "country",
                    "latitude", "longitude", "email", "website", "created_by"
                },
                values: new object[]
                {
                    restaurantInfoId,
                    "Rumi Restaurant",
                    "Rue du Grand-Pré 45",
                    null,
                    "Genève",
                    "1202",
                    "Switzerland",
                    null,
                    null,
                    "rumigeneve@gmail.com",
                    null,
                    "AddRestaurantInfo migration"
                });
            migrationBuilder.InsertData(
                table: "RestaurantPhoneNumbers",
                columns: new[]
                {
                    "id", "restaurant_info_id", "label", "number", "whats_app_enabled",
                    "display_order", "is_active", "created_by"
                },
                values: new object[]
                {
                    phoneId,
                    restaurantInfoId,
                    "Reception",
                    "+41227863333",
                    false,
                    0,
                    true,
                    "AddRestaurantInfo migration"
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestaurantPhoneNumbers");

            migrationBuilder.DropTable(
                name: "RestaurantInfo");
        }
    }
}
