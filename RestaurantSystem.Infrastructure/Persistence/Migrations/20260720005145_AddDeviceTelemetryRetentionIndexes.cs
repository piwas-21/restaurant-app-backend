using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceTelemetryRetentionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_DeviceOrderReceipts_created_at",
                table: "DeviceOrderReceipts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvents_created_at",
                table: "DeviceEvents",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceOrderReceipts_created_at",
                table: "DeviceOrderReceipts");

            migrationBuilder.DropIndex(
                name: "IX_DeviceEvents_created_at",
                table: "DeviceEvents");
        }
    }
}
