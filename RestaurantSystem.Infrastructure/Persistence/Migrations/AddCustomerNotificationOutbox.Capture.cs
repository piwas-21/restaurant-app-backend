using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddCustomerNotificationOutbox
{
    private static void InstallCapture(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION capture_customer_order_notification() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP = 'DELETE' THEN
                DELETE FROM customer_notification_watches WHERE subject_kind = 'order' AND subject_id = OLD.id;
                RETURN NULL;
            END IF;
            IF NEW.is_deleted THEN
                DELETE FROM customer_notification_watches WHERE subject_kind = 'order' AND subject_id = NEW.id;
                RETURN NULL;
            END IF;
            IF TG_OP = 'UPDATE' THEN
                IF ROW(NEW.status, NEW.payment_status, NEW.effective_preparation_minutes, NEW.expected_ready_at,
                       NEW.estimated_delivery_time, NEW.actual_delivery_time)
                    IS NOT DISTINCT FROM
                   ROW(OLD.status, OLD.payment_status, OLD.effective_preparation_minutes, OLD.expected_ready_at,
                       OLD.estimated_delivery_time, OLD.actual_delivery_time) THEN
                    RETURN NULL;
                END IF;
            END IF;
            -- Lock the selected watch before the FK check: a concurrent withdrawal either wins
            -- first (no capture) or waits and then cascades this hint. An old SELECT snapshot
            -- must not cause a foreign-key violation after withdrawal wins.
            INSERT INTO customer_notification_events (subject_kind, subject_id)
                SELECT subject_kind, subject_id FROM customer_notification_watches
                WHERE subject_kind = 'order' AND subject_id = NEW.id FOR KEY SHARE;
            RETURN NULL;
        END $$;
        CREATE TRIGGER customer_order_notification AFTER INSERT OR UPDATE OR DELETE ON orders
            FOR EACH ROW EXECUTE FUNCTION capture_customer_order_notification();

        CREATE FUNCTION capture_customer_reservation_notification() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            IF TG_OP = 'DELETE' THEN
                DELETE FROM customer_notification_watches WHERE subject_kind = 'reservation' AND subject_id = OLD.id;
                RETURN NULL;
            END IF;
            IF TG_OP = 'UPDATE' THEN
                IF ROW(NEW.status, NEW.reservation_date, NEW.start_time, NEW.end_time, NEW.number_of_guests)
                    IS NOT DISTINCT FROM
                   ROW(OLD.status, OLD.reservation_date, OLD.start_time, OLD.end_time, OLD.number_of_guests) THEN
                    RETURN NULL;
                END IF;
            END IF;
            INSERT INTO customer_notification_events (subject_kind, subject_id)
                SELECT subject_kind, subject_id FROM customer_notification_watches
                WHERE subject_kind = 'reservation' AND subject_id = NEW.id FOR KEY SHARE;
            RETURN NULL;
        END $$;
        CREATE TRIGGER customer_reservation_notification AFTER INSERT OR UPDATE OR DELETE ON "Reservations"
            FOR EACH ROW EXECUTE FUNCTION capture_customer_reservation_notification();
        """);

    private static void RemoveCapture(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Lock producers before the queue, preserving their write order. Refuse instead of
        -- waiting while another transaction holds the opposite side of the lock chain.
        LOCK TABLE orders, "Reservations", customer_notification_watches, customer_notification_events
            IN ACCESS EXCLUSIVE MODE NOWAIT;
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM customer_notification_watches)
                OR EXISTS (SELECT 1 FROM customer_notification_events) THEN
                RAISE EXCEPTION 'Customer notification capture must be withdrawn before downgrade' USING ERRCODE = '23514';
            END IF;
        END $$;
        DROP TRIGGER customer_order_notification ON orders;
        DROP TRIGGER customer_reservation_notification ON "Reservations";
        DROP FUNCTION capture_customer_order_notification();
        DROP FUNCTION capture_customer_reservation_notification();
        """);
}
