using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOptionSetMaterializationOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OptionSetAppliedRows_row_type_materialized_row_id",
                table: "OptionSetAppliedRows");

            migrationBuilder.AddColumn<bool>(
                name: "is_enabled",
                table: "OptionSetEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "owns_materialized_row",
                table: "OptionSetAppliedRows",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAppliedRows_row_type_materialized_row_id",
                table: "OptionSetAppliedRows",
                columns: new[] { "row_type", "materialized_row_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OptionSetAppliedRows_row_type_materialized_row_id",
                table: "OptionSetAppliedRows");

            migrationBuilder.DropColumn(
                name: "is_enabled",
                table: "OptionSetEntries");

            migrationBuilder.DropColumn(
                name: "owns_materialized_row",
                table: "OptionSetAppliedRows");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAppliedRows_row_type_materialized_row_id",
                table: "OptionSetAppliedRows",
                columns: new[] { "row_type", "materialized_row_id" },
                unique: true);
        }
    }
}
