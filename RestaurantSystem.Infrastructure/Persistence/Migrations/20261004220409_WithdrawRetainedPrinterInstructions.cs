using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WithdrawRetainedPrinterInstructions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "supports_update_authorization",
                table: "PrinterDevices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "withdrawn_at",
                table: "OrderOperationalNotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FeedEventAt",
                table: "OrderOperationalNotes",
                type: "timestamp with time zone",
                nullable: false,
                computedColumnSql: "COALESCE(withdrawn_at, created_at)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderOperationalNotes_FeedEventAt_id",
                table: "OrderOperationalNotes",
                columns: new[] { "FeedEventAt", "id" });

            migrationBuilder.Sql(RetainedTextErasureUp);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "OrderOperationalNotes" WHERE withdrawn_at IS NOT NULL) THEN
                        RAISE EXCEPTION 'Retained printer withdrawal evidence prevents this downgrade';
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql(RetainedTextErasureDown);

            migrationBuilder.DropColumn(
                name: "supports_update_authorization",
                table: "PrinterDevices");

            migrationBuilder.DropIndex(
                name: "IX_OrderOperationalNotes_FeedEventAt_id",
                table: "OrderOperationalNotes");

            migrationBuilder.DropColumn(
                name: "FeedEventAt",
                table: "OrderOperationalNotes");

            migrationBuilder.DropColumn(
                name: "withdrawn_at",
                table: "OrderOperationalNotes");
        }
    }
}
