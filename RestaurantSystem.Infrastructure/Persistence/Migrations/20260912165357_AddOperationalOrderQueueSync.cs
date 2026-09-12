using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Companion metadata is generated in 20260912165357_AddOperationalOrderQueueSync.Designer.cs;
    /// ApplicationDbContextModelSnapshot contains the matching OrderChange/last-change model.
    /// </summary>
    public partial class AddOperationalOrderQueueSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Each tenant has its own database. The sequence is therefore tenant-scoped. The
            // transaction-scoped advisory lock makes sequence allocation follow commit order: a
            // writer that has reserved a number keeps the lock until commit or rollback, so a
            // later writer cannot pass a snapshot watermark while the first write is uncommitted.
            migrationBuilder.Sql("""
                CREATE SEQUENCE order_change_sequence
                    AS bigint
                    START WITH 1
                    INCREMENT BY 1;

                CREATE FUNCTION next_order_queue_sequence()
                RETURNS bigint
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    PERFORM pg_advisory_xact_lock(
                        hashtextextended('restaurant-system.order-change-sequence', 0));
                    RETURN nextval('order_change_sequence');
                END;
                $function$;
                """);

            migrationBuilder.AddColumn<long>(
                name: "last_change_sequence",
                table: "orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "order_changes",
                columns: table => new
                {
                    sequence = table.Column<long>(
                        type: "bigint",
                        nullable: false,
                        defaultValueSql: "next_order_queue_sequence()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_changes", x => x.sequence);
                    table.ForeignKey(
                        name: "fk_order_changes_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orders_last_change_sequence",
                table: "orders",
                column: "last_change_sequence");

            migrationBuilder.CreateIndex(
                name: "ix_order_changes_order_id",
                table: "order_changes",
                column: "order_id");

            // The order row and every persisted child that contributes to OrderDto are journaled
            // in the database, not in individual handlers. This covers HTTP commands, background
            // jobs, imports, and direct SQL writes through one atomic transaction boundary.
            // The journal records mutation facts only. ReadChanges applies the authoritative
            // operational filter and turns an absent current row into a typed removal.
            migrationBuilder.Sql("""
                CREATE FUNCTION assign_order_queue_sequence()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF pg_trigger_depth() > 1 THEN
                            RETURN OLD;
                        END IF;

                        INSERT INTO order_changes (sequence, order_id, kind, reason)
                        VALUES (next_order_queue_sequence(), OLD.id, 'Remove', 'deleted');
                        RETURN OLD;
                    END IF;

                    -- A child trigger's UPDATE already assigned NEW.last_change_sequence. Returning
                    -- NEW unchanged preserves that value while avoiding a duplicate sequence/journal row.
                    IF pg_trigger_depth() > 1 THEN
                        RETURN NEW;
                    END IF;

                    NEW.last_change_sequence := next_order_queue_sequence();
                    RETURN NEW;
                END;
                $function$;
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION record_order_queue_row_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF pg_trigger_depth() > 1 THEN
                        RETURN NEW;
                    END IF;

                    INSERT INTO order_changes (sequence, order_id, kind, reason)
                    VALUES (NEW.last_change_sequence, NEW.id, 'Upsert', NULL);
                    RETURN NEW;
                END;
                $function$;
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION record_order_queue_child_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    order_id uuid;
                    sequence_number bigint;
                BEGIN
                    IF pg_trigger_depth() > 1 THEN
                        IF TG_OP = 'DELETE' THEN
                            RETURN OLD;
                        END IF;
                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        order_id := OLD.order_id;
                    ELSE
                        order_id := NEW.order_id;
                    END IF;

                    -- A child DELETE caused by a hard order DELETE runs after its parent is gone;
                    -- the parent trigger already recorded the removal event.
                    IF NOT EXISTS (SELECT 1 FROM orders WHERE id = order_id) THEN
                        IF TG_OP = 'DELETE' THEN
                            RETURN OLD;
                        END IF;
                        RETURN NEW;
                    END IF;

                    sequence_number := next_order_queue_sequence();
                    UPDATE orders
                    SET last_change_sequence = sequence_number
                    WHERE id = order_id;

                    INSERT INTO order_changes (sequence, order_id, kind, reason)
                    VALUES (sequence_number, order_id, 'Upsert', NULL);

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;
                    RETURN NEW;
                END;
                $function$;
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION record_order_item_ingredient_queue_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    item_id uuid;
                    order_id uuid;
                    sequence_number bigint;
                BEGIN
                    IF pg_trigger_depth() > 1 THEN
                        IF TG_OP = 'DELETE' THEN
                            RETURN OLD;
                        END IF;
                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        item_id := OLD.order_item_id;
                    ELSE
                        item_id := NEW.order_item_id;
                    END IF;
                    SELECT item.order_id INTO order_id
                    FROM "OrderItems" AS item
                    WHERE item.id = item_id;

                    IF order_id IS NULL OR NOT EXISTS (SELECT 1 FROM orders WHERE id = order_id) THEN
                        IF TG_OP = 'DELETE' THEN
                            RETURN OLD;
                        END IF;
                        RETURN NEW;
                    END IF;

                    sequence_number := next_order_queue_sequence();
                    UPDATE orders
                    SET last_change_sequence = sequence_number
                    WHERE id = order_id;

                    INSERT INTO order_changes (sequence, order_id, kind, reason)
                    VALUES (sequence_number, order_id, 'Upsert', NULL);

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;
                    RETURN NEW;
                END;
                $function$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER orders_queue_sequence
                BEFORE INSERT OR UPDATE OR DELETE ON orders
                FOR EACH ROW EXECUTE FUNCTION assign_order_queue_sequence();

                CREATE TRIGGER orders_queue_change
                AFTER INSERT OR UPDATE ON orders
                FOR EACH ROW EXECUTE FUNCTION record_order_queue_row_change();

                CREATE TRIGGER order_items_queue_change
                AFTER INSERT OR UPDATE OR DELETE ON "OrderItems"
                FOR EACH ROW EXECUTE FUNCTION record_order_queue_child_change();

                CREATE TRIGGER order_item_ingredients_queue_change
                AFTER INSERT OR UPDATE OR DELETE ON "OrderItemIngredients"
                FOR EACH ROW EXECUTE FUNCTION record_order_item_ingredient_queue_change();

                CREATE TRIGGER order_payments_queue_change
                AFTER INSERT OR UPDATE OR DELETE ON order_payments
                FOR EACH ROW EXECUTE FUNCTION record_order_queue_child_change();

                CREATE TRIGGER order_status_history_queue_change
                AFTER INSERT OR UPDATE OR DELETE ON "OrderStatusHistory"
                FOR EACH ROW EXECUTE FUNCTION record_order_queue_child_change();

                CREATE TRIGGER order_operational_notes_queue_change
                AFTER INSERT OR UPDATE OR DELETE ON "OrderOperationalNotes"
                FOR EACH ROW EXECUTE FUNCTION record_order_queue_child_change();

                CREATE TRIGGER order_addresses_queue_change
                AFTER INSERT OR UPDATE OR DELETE ON "OrderAddresses"
                FOR EACH ROW EXECUTE FUNCTION record_order_queue_child_change();

                CREATE TRIGGER order_checkout_sessions_queue_change
                AFTER INSERT OR UPDATE OR DELETE ON order_checkout_sessions
                FOR EACH ROW EXECUTE FUNCTION record_order_queue_child_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS order_checkout_sessions_queue_change ON order_checkout_sessions;
                DROP TRIGGER IF EXISTS order_addresses_queue_change ON "OrderAddresses";
                DROP TRIGGER IF EXISTS order_operational_notes_queue_change ON "OrderOperationalNotes";
                DROP TRIGGER IF EXISTS order_status_history_queue_change ON "OrderStatusHistory";
                DROP TRIGGER IF EXISTS order_payments_queue_change ON order_payments;
                DROP TRIGGER IF EXISTS order_item_ingredients_queue_change ON "OrderItemIngredients";
                DROP TRIGGER IF EXISTS order_items_queue_change ON "OrderItems";
                DROP TRIGGER IF EXISTS orders_queue_change ON orders;
                DROP TRIGGER IF EXISTS orders_queue_sequence ON orders;
                """);

            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS record_order_item_ingredient_queue_change();
                DROP FUNCTION IF EXISTS record_order_queue_child_change();
                DROP FUNCTION IF EXISTS record_order_queue_row_change();
                DROP FUNCTION IF EXISTS assign_order_queue_sequence();
                """);

            migrationBuilder.DropTable(
                name: "order_changes");

            migrationBuilder.DropIndex(
                name: "IX_orders_last_change_sequence",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "last_change_sequence",
                table: "orders");

            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS next_order_queue_sequence();
                DROP SEQUENCE IF EXISTS order_change_sequence;
                """);
        }
    }
}
