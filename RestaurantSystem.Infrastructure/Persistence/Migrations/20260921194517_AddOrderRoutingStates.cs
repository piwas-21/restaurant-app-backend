using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderRoutingStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kitchen_routing_mode",
                table: "PrinterDevices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Stations");

            migrationBuilder.CreateTable(
                name: "OrderRoutingStates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    target = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_routing_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_routing_states_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrinterDeviceTargetCapabilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_supported = table.Column<bool>(type: "boolean", nullable: false),
                    is_configured = table.Column<bool>(type: "boolean", nullable: false),
                    auto_print_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    printer_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    reported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_printer_device_target_capabilities", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRoutingStates_job_id_revision_target",
                table: "OrderRoutingStates",
                columns: new[] { "job_id", "revision", "target" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderRoutingStates_order_id_created_at",
                table: "OrderRoutingStates",
                columns: new[] { "order_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_PrinterDeviceTargetCapabilities_device_id_target",
                table: "PrinterDeviceTargetCapabilities",
                columns: new[] { "device_id", "target" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrinterDeviceTargetCapabilities_target_reported_at",
                table: "PrinterDeviceTargetCapabilities",
                columns: new[] { "target", "reported_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderRoutingStates");

            migrationBuilder.DropTable(
                name: "PrinterDeviceTargetCapabilities");

            migrationBuilder.DropColumn(
                name: "kitchen_routing_mode",
                table: "PrinterDevices");
        }
    }
}
