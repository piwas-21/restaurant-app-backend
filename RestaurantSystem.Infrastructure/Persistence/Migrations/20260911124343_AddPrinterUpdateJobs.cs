using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinterUpdateJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceOrderReceipts_order_id_device_id_target",
                table: "DeviceOrderReceipts");

            migrationBuilder.AddColumn<Guid>(
                name: "job_id",
                table: "DeviceOrderReceipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "job_type",
                table: "DeviceOrderReceipts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "revision",
                table: "DeviceOrderReceipts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceOrderReceipts_device_id_job_id_revision_target",
                table: "DeviceOrderReceipts",
                columns: new[] { "device_id", "job_id", "revision", "target" },
                unique: true,
                filter: "\"job_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceOrderReceipts_order_id_device_id_target",
                table: "DeviceOrderReceipts",
                columns: new[] { "order_id", "device_id", "target" },
                unique: true,
                filter: "\"job_id\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceOrderReceipts_device_id_job_id_revision_target",
                table: "DeviceOrderReceipts");

            migrationBuilder.DropIndex(
                name: "IX_DeviceOrderReceipts_order_id_device_id_target",
                table: "DeviceOrderReceipts");

            migrationBuilder.DropColumn(
                name: "job_id",
                table: "DeviceOrderReceipts");

            migrationBuilder.DropColumn(
                name: "job_type",
                table: "DeviceOrderReceipts");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "DeviceOrderReceipts");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceOrderReceipts_order_id_device_id_target",
                table: "DeviceOrderReceipts",
                columns: new[] { "order_id", "device_id", "target" },
                unique: true);
        }
    }
}
