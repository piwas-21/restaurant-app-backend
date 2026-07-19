using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrinterDevices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    tenant_slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    platform = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    app_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    last_heartbeat_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    feed_running = table.Column<bool>(type: "boolean", nullable: false),
                    last_successful_poll_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    api_base_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    kitchen_printer = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    cashier_printer = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_printer_devices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrinterDevices_device_id",
                table: "PrinterDevices",
                column: "device_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrinterDevices_last_heartbeat_at",
                table: "PrinterDevices",
                column: "last_heartbeat_at");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterDevices_tenant_slug",
                table: "PrinterDevices",
                column: "tenant_slug");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrinterDevices");
        }
    }
}
