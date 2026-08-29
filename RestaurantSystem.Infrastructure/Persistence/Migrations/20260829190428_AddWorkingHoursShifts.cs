using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkingHoursShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "working_hours_shifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    working_hours_id = table.Column<Guid>(type: "uuid", nullable: false),
                    open_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    close_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_working_hours_shifts", x => x.id);
                    table.ForeignKey(
                        name: "fk_working_hours_shifts_working_hours_working_hours_id",
                        column: x => x.working_hours_id,
                        principalTable: "working_hours",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_working_hours_shifts_working_hours_id_open_time",
                table: "working_hours_shifts",
                columns: new[] { "working_hours_id", "open_time" });

            // Backfill: every existing day becomes a single-shift day holding exactly the window it
            // already had. This migration READS working_hours and writes nothing back to it — no
            // column is altered, renamed or dropped — so a tenant that is live on the old schema
            // keeps its hours byte for byte, and Down() is a plain DropTable that restores the old
            // world with no data restore. The table was created three statements ago and is empty,
            // so there is nothing to conflict with.
            migrationBuilder.Sql(
                """
                INSERT INTO working_hours_shifts (id, working_hours_id, open_time, close_time, created_at, created_by)
                SELECT gen_random_uuid(), wh.id, wh.open_time, wh.close_time, CURRENT_TIMESTAMP, 'Migration:AddWorkingHoursShifts'
                FROM working_hours wh;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "working_hours_shifts");
        }
    }
}
