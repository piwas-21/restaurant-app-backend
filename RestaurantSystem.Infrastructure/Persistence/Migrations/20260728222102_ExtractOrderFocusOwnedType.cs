using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Moves the five focus columns behind the owned <c>Order.Focus</c> type. Four of them keep
    /// their column exactly; <c>is_focus_order</c> goes, because "is this order focused" is now
    /// read as "is focused_at non-null".
    /// </summary>
    /// <remarks>
    /// The two columns agree on every row the current code writes, but they were independent for
    /// long enough that this cannot be assumed of historical rows, and after the drop a
    /// disagreement is silent in either direction — a focused order with no focused_at reads back
    /// unfocused, a stale focused_at on an unfocused order resurrects it onto the cashier's focus
    /// list. So the flag is reconciled into the timestamp before it is dropped, while it is still
    /// there to be believed.
    /// </remarks>
    public partial class ExtractOrderFocusOwnedType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Focused, but nothing to prove it by once the flag is gone. created_at is the
            // conservative stamp: the order cannot have been focused before it existed, and
            // GetFocusOrders only sorts by this, so a too-early value costs ordering, not presence.
            migrationBuilder.Sql("""
                UPDATE orders
                SET focused_at = COALESCE(updated_at, created_at)
                WHERE is_focus_order AND focused_at IS NULL;
                """);

            // The mirror image: leftovers from before un-focusing cleared all four columns would
            // read back as focused.
            migrationBuilder.Sql("""
                UPDATE orders
                SET priority = NULL, focus_reason = NULL, focused_at = NULL, focused_by = NULL
                WHERE NOT is_focus_order AND focused_at IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_orders_is_focus_order",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_is_focus_order_priority",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "is_focus_order",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "IX_orders_priority_focused_at",
                table: "orders",
                columns: new[] { "priority", "focused_at" },
                filter: "\"focused_at\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_priority_focused_at",
                table: "orders");

            migrationBuilder.AddColumn<bool>(
                name: "is_focus_order",
                table: "orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Rebuild the flag from the timestamp that replaced it, so a rollback keeps the focus
            // list intact instead of re-adding an all-false column.
            migrationBuilder.Sql("UPDATE orders SET is_focus_order = (focused_at IS NOT NULL);");

            migrationBuilder.CreateIndex(
                name: "IX_orders_is_focus_order",
                table: "orders",
                column: "is_focus_order");

            migrationBuilder.CreateIndex(
                name: "IX_orders_is_focus_order_priority",
                table: "orders",
                columns: new[] { "is_focus_order", "priority" });
        }
    }
}
