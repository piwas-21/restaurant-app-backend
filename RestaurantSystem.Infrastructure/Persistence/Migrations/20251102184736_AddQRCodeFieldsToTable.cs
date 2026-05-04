using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQRCodeFieldsToTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "q_r_code_data",
                table: "Tables",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "q_r_code_generated_at",
                table: "Tables",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "q_r_code_data",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "q_r_code_generated_at",
                table: "Tables");
        }
    }
}
