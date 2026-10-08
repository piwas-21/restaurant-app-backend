using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKitchenBoardWorkCompletions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE SEQUENCE kitchen_board_change_sequence AS bigint START WITH 1;
                CREATE SEQUENCE kitchen_board_work_completion_sequence AS bigint START WITH 1;

                CREATE FUNCTION next_kitchen_board_change_sequence()
                RETURNS bigint
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    -- Keep the global queue lock first, matching OrderOperationalNotes' existing
                    -- AFTER trigger. The board reader takes these same locks in this order.
                    PERFORM pg_advisory_xact_lock(
                        hashtextextended('restaurant-system.order-change-sequence', 0));
                    PERFORM pg_advisory_xact_lock(664311, 2);
                    RETURN nextval('kitchen_board_change_sequence');
                END;
                $function$;

                CREATE FUNCTION next_kitchen_board_completion_sequence()
                RETURNS bigint
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    -- All feed watermarks use the shared order-change lock before the board lock.
                    -- This both preserves commit-order snapshots and lets a completion advance its
                    -- correction note in the same transaction without reversing lock order.
                    PERFORM pg_advisory_xact_lock(
                        hashtextextended('restaurant-system.order-change-sequence', 0));
                    PERFORM pg_advisory_xact_lock(664311, 2);
                    RETURN nextval('kitchen_board_work_completion_sequence');
                END;
                $function$;

                CREATE FUNCTION assign_kitchen_board_note_sequence()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    NEW.kitchen_board_sequence := next_kitchen_board_change_sequence();
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER order_operational_notes_kitchen_board_sequence
                    BEFORE UPDATE OF text, withdrawn_at, kitchen_changes_json
                    ON "OrderOperationalNotes"
                    FOR EACH ROW
                    EXECUTE FUNCTION assign_kitchen_board_note_sequence();
                """);

            migrationBuilder.AddColumn<long>(
                name: "kitchen_board_sequence",
                table: "OrderOperationalNotes",
                type: "bigint",
                nullable: false,
                defaultValueSql: "next_kitchen_board_change_sequence()");

            migrationBuilder.CreateTable(
                name: "kitchen_board_work_completions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    acknowledged_order_version = table.Column<int>(type: "integer", nullable: false),
                    account_revision = table.Column<long>(type: "bigint", nullable: true),
                    sequence = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "next_kitchen_board_completion_sequence()"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kitchen_board_work_completions", x => x.id);
                    table.CheckConstraint("ck_kitchen_board_work_completion_values", "acknowledged_order_version > 0 AND (account_revision IS NULL OR account_revision > 0)");
                    table.ForeignKey(
                        name: "fk_kitchen_board_work_completions_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderOperationalNotes_kitchen_board_sequence",
                table: "OrderOperationalNotes",
                column: "kitchen_board_sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_board_work_completions_order_id_work_item_id_kind",
                table: "kitchen_board_work_completions",
                columns: new[] { "order_id", "work_item_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_kitchen_board_work_completions_sequence",
                table: "kitchen_board_work_completions",
                column: "sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS order_operational_notes_kitchen_board_sequence
                    ON "OrderOperationalNotes";
                """);

            migrationBuilder.DropTable(
                name: "kitchen_board_work_completions");

            migrationBuilder.DropIndex(
                name: "IX_OrderOperationalNotes_kitchen_board_sequence",
                table: "OrderOperationalNotes");

            migrationBuilder.DropColumn(
                name: "kitchen_board_sequence",
                table: "OrderOperationalNotes");

            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS assign_kitchen_board_note_sequence();
                DROP FUNCTION IF EXISTS next_kitchen_board_completion_sequence();
                DROP FUNCTION IF EXISTS next_kitchen_board_change_sequence();
                DROP SEQUENCE IF EXISTS kitchen_board_work_completion_sequence;
                DROP SEQUENCE IF EXISTS kitchen_board_change_sequence;
                """);
        }
    }
}
