using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StaffOrderSimplification : Migration
    {
        public const string BackfillSql = """
            WITH RECURSIVE roots AS (
                SELECT a.source_order_id AS root_id, a.supplement_order_id AS order_id,
                       ARRAY[a.source_order_id, a.supplement_order_id] AS visited
                FROM order_amendments a
                WHERE a.state = 'Committed' AND a.supplement_order_id IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM order_amendments parent
                      WHERE parent.state = 'Committed' AND parent.supplement_order_id = a.source_order_id)
                UNION ALL
                SELECT roots.root_id, a.supplement_order_id, roots.visited || a.supplement_order_id
                FROM roots JOIN order_amendments a ON a.source_order_id = roots.order_id
                WHERE a.state = 'Committed' AND a.supplement_order_id IS NOT NULL
                  AND NOT a.supplement_order_id = ANY(roots.visited)
            )
            UPDATE orders SET amendment_root_order_id = roots.root_id
            FROM roots WHERE orders.id = roots.order_id AND orders.amendment_root_order_id IS NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "admission_id", table: "table_guest_participants", type: "uuid", nullable: true,
                oldClrType: typeof(Guid), oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "amendment_root_order_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(BackfillSql);

            migrationBuilder.CreateIndex(
                name: "IX_orders_amendment_root_order_id",
                table: "orders",
                column: "amendment_root_order_id");

            migrationBuilder.AddForeignKey(
                name: "FK_orders_orders_amendment_root_order_id",
                table: "orders",
                column: "amendment_root_order_id",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Old clients require an admission reference. Never invent a credential for QR-only guests.
            migrationBuilder.Sql("""
                DO $block$ BEGIN
                    IF EXISTS (SELECT 1 FROM table_guest_participants WHERE admission_id IS NULL) THEN
                        RAISE EXCEPTION 'QR-only participants prevent rollback to required admission codes';
                    END IF;
                END $block$;
                """);
            migrationBuilder.DropForeignKey(name: "FK_orders_orders_amendment_root_order_id", table: "orders");
            migrationBuilder.DropIndex(name: "IX_orders_amendment_root_order_id", table: "orders");
            migrationBuilder.DropColumn(name: "amendment_root_order_id", table: "orders");
            migrationBuilder.AlterColumn<Guid>(
                name: "admission_id", table: "table_guest_participants", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
        }
    }
}
