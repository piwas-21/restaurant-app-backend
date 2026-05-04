using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderTypeConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_type_configurations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_type = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_type_configurations", x => x.id);
                });

            // Seed data: Initialize all order types as enabled
            migrationBuilder.InsertData(
                table: "order_type_configurations",
                columns: new[] { "id", "order_type", "is_enabled", "display_order", "created_at", "created_by" },
                values: new object[,]
                {
                    { Guid.NewGuid(), 1, true, 1, DateTime.UtcNow, "System" }, // DineIn
                    { Guid.NewGuid(), 2, true, 2, DateTime.UtcNow, "System" }, // Takeaway
                    { Guid.NewGuid(), 3, true, 3, DateTime.UtcNow, "System" }  // Delivery
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_type_configurations");
        }
    }
}
