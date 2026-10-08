using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdvanceKitchenBoardOnOrderSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION advance_kitchen_board_notes_on_order_visibility_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NEW.is_deleted IS NOT DISTINCT FROM OLD.is_deleted THEN
                        RETURN NEW;
                    END IF;

                    -- Preserve the same commit-ordered lock sequence used by board snapshots.
                    PERFORM pg_advisory_xact_lock(
                        hashtextextended('restaurant-system.order-change-sequence', 0));
                    PERFORM pg_advisory_xact_lock(664311, 2);

                    UPDATE "OrderOperationalNotes" AS note
                    SET kitchen_board_sequence = next_kitchen_board_change_sequence()
                    WHERE note.order_id = NEW.id
                        AND note.audience = 'Kitchen'
                        AND note.kitchen_changes_json IS NOT NULL
                        AND note.withdrawn_at IS NULL;

                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER orders_kitchen_board_visibility_change
                    AFTER UPDATE OF is_deleted ON orders
                    FOR EACH ROW
                    WHEN (OLD.is_deleted IS DISTINCT FROM NEW.is_deleted)
                    EXECUTE FUNCTION advance_kitchen_board_notes_on_order_visibility_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS orders_kitchen_board_visibility_change ON orders;
                DROP FUNCTION IF EXISTS advance_kitchen_board_notes_on_order_visibility_change();
                """);
        }
    }
}
