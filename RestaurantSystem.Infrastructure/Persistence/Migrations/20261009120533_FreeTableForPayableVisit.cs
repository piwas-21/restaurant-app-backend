using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FreeTableForPayableVisit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_table_service_sessions_table_id",
                table: "table_service_sessions");

            migrationBuilder.DropIndex(
                name: "IX_table_service_sessions_table_number",
                table: "table_service_sessions");

            migrationBuilder.AddColumn<DateTime>(
                name: "released_at",
                table: "table_service_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "released_by",
                table: "table_service_sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_table_service_sessions_table_id",
                table: "table_service_sessions",
                column: "table_id",
                unique: true,
                filter: "\"status\" = 'Open' AND \"released_at\" IS NULL AND \"table_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_table_service_sessions_table_number",
                table: "table_service_sessions",
                column: "table_number",
                unique: true,
                filter: "\"status\" = 'Open' AND \"released_at\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_table_service_sessions_table_id",
                table: "table_service_sessions");

            migrationBuilder.DropIndex(
                name: "IX_table_service_sessions_table_number",
                table: "table_service_sessions");

            migrationBuilder.DropColumn(
                name: "released_at",
                table: "table_service_sessions");

            migrationBuilder.DropColumn(
                name: "released_by",
                table: "table_service_sessions");

            migrationBuilder.CreateIndex(
                name: "ix_table_service_sessions_table_id",
                table: "table_service_sessions",
                column: "table_id",
                unique: true,
                filter: "\"status\" = 'Open' AND \"table_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_table_service_sessions_table_number",
                table: "table_service_sessions",
                column: "table_number",
                unique: true,
                filter: "\"status\" = 'Open'");
        }
    }
}
