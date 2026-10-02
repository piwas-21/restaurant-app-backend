using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredKitchenChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "account_revision",
                table: "OrderOperationalNotes",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "amendment_id",
                table: "OrderOperationalNotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kitchen_changes_json",
                table: "OrderOperationalNotes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kitchen_target",
                table: "OrderOperationalNotes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_operational_notes_kitchen_change",
                table: "OrderOperationalNotes",
                sql: "(kitchen_changes_json IS NULL AND amendment_id IS NULL AND kitchen_target IS NULL AND account_revision IS NULL) OR (kitchen_changes_json IS NOT NULL AND amendment_id IS NOT NULL AND kitchen_target IS NOT NULL AND audience = 'Kitchen' AND (account_revision IS NULL OR account_revision > 0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_order_operational_notes_kitchen_change",
                table: "OrderOperationalNotes");

            migrationBuilder.DropColumn(
                name: "account_revision",
                table: "OrderOperationalNotes");

            migrationBuilder.DropColumn(
                name: "amendment_id",
                table: "OrderOperationalNotes");

            migrationBuilder.DropColumn(
                name: "kitchen_changes_json",
                table: "OrderOperationalNotes");

            migrationBuilder.DropColumn(
                name: "kitchen_target",
                table: "OrderOperationalNotes");
        }
    }
}
