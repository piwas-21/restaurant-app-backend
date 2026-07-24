using System;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantSystem.Infrastructure.Persistence.Support;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFloorPlanAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "shape",
                table: "Tables",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<Guid>(
                name: "floor_plan_id",
                table: "Tables",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FloorPlans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    width_meters = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    height_meters = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    grid_size_cm = table.Column<int>(type: "integer", nullable: false, defaultValue: 25),
                    background_style = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "plain"),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_floor_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "FloorPlanItems",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    floor_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    x = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    y = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    width_meters = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    height_meters = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    rotation_degrees = table.Column<decimal>(type: "numeric(5,1)", nullable: false),
                    z_index = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    style_variant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_floor_plan_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_floor_plan_items_floor_plans_floor_plan_id",
                        column: x => x.floor_plan_id,
                        principalTable: "FloorPlans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FloorPlanWalls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    floor_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    points_json = table.Column<string>(type: "jsonb", nullable: false),
                    thickness_meters = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0.12m),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    room_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    floor_style = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    z_index = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_floor_plan_walls", x => x.id);
                    table.ForeignKey(
                        name: "fk_floor_plan_walls_floor_plans_floor_plan_id",
                        column: x => x.floor_plan_id,
                        principalTable: "FloorPlans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FloorPlanOpenings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    wall_id = table.Column<Guid>(type: "uuid", nullable: false),
                    segment_index = table.Column<int>(type: "integer", nullable: false),
                    offset_meters = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    width_meters = table.Column<decimal>(type: "numeric(6,2)", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    swing_direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "none"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_floor_plan_openings", x => x.id);
                    table.ForeignKey(
                        name: "fk_floor_plan_openings_floorplanwalls_wall_id",
                        column: x => x.wall_id,
                        principalTable: "FloorPlanWalls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tables_floor_plan_id",
                table: "Tables",
                column: "floor_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_floor_plan_items_floor_plan_id",
                table: "FloorPlanItems",
                column: "floor_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_floor_plan_openings_wall_id",
                table: "FloorPlanOpenings",
                column: "wall_id");

            migrationBuilder.CreateIndex(
                name: "IX_FloorPlans_is_default",
                table: "FloorPlans",
                column: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_floor_plan_walls_floor_plan_id",
                table: "FloorPlanWalls",
                column: "floor_plan_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tables_floor_plans_floor_plan_id",
                table: "Tables",
                column: "floor_plan_id",
                principalTable: "FloorPlans",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // ── FLOOR-PLAN-REVAMP §6: legacy pixel units → metres ──────────────
            // Runs only on an install that already has tables AND no plan yet —
            // i.e. an existing tenant (RUMI prod, demo, staging), never a fresh
            // database (where Tables is empty at migration time and the seeders
            // build the reference plan instead). Prod's intact Shape/Rotation
            // values are preserved by converting in place; demo/staging Width=0
            // rows (defect 8) fall back to a seats-derived footprint. A minimal
            // 12×10 m room is created to hold the converted tables — the owner
            // draws real walls/decor in the editor. SQL is shared with the
            // conversion test via FloorPlanMigrationSql (single source of truth).
            migrationBuilder.Sql($@"
DO $$
DECLARE plan_id uuid;
BEGIN
    IF EXISTS (SELECT 1 FROM ""Tables"") AND NOT EXISTS (SELECT 1 FROM ""FloorPlans"") THEN
        plan_id := gen_random_uuid();
        INSERT INTO ""FloorPlans""
            (id, name, width_meters, height_meters, grid_size_cm, background_style, is_default, display_order, created_at, created_by)
        VALUES
            (plan_id, 'Main floor', {FloorPlanMigrationSql.RoomWidthMeters.ToString(CultureInfo.InvariantCulture)}, {FloorPlanMigrationSql.RoomHeightMeters.ToString(CultureInfo.InvariantCulture)}, 25, 'plain', true, 0, CURRENT_TIMESTAMP, 'System');
        {FloorPlanMigrationSql.ConvertTablesToMetresTemplate.Replace("{PLAN_ID}", "plan_id")}
    END IF;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tables_floor_plans_floor_plan_id",
                table: "Tables");

            migrationBuilder.DropTable(
                name: "FloorPlanItems");

            migrationBuilder.DropTable(
                name: "FloorPlanOpenings");

            migrationBuilder.DropTable(
                name: "FloorPlanWalls");

            migrationBuilder.DropTable(
                name: "FloorPlans");

            migrationBuilder.DropIndex(
                name: "ix_tables_floor_plan_id",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "floor_plan_id",
                table: "Tables");

            migrationBuilder.AlterColumn<string>(
                name: "shape",
                table: "Tables",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
