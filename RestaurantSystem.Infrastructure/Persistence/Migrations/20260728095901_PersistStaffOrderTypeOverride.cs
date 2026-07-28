using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ORDER-TYPE-AVAILABILITY-PLAN §9.6 — a staff member may accept an order containing items that
    /// are not available for its channel (warn-and-allow, by design). Until now the only trace was an
    /// application-log line, which no owner reads and which rotates.
    /// </summary>
    /// <remarks>
    /// Purely additive: two nullable columns, no backfill, no index, no lock beyond the catalog
    /// update. Existing rows read as "no override", which is exactly what they are — the guard has
    /// been live since the feature shipped and simply left nothing behind. Safe to run against a
    /// live database and safe to roll back (the <c>Down</c> drops data that nothing depended on
    /// before this release).
    /// </remarks>
    public partial class PersistStaffOrderTypeOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "order_type_override_by",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "order_type_override_items",
                table: "orders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "order_type_override_by",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "order_type_override_items",
                table: "orders");
        }
    }
}
