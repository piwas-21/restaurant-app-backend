using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "revision",
                table: "Reservations",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");
            migrationBuilder.Sql("""
                CREATE FUNCTION rotate_reservation_revision() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    NEW.revision := gen_random_uuid();
                    RETURN NEW;
                END $$;
                CREATE TRIGGER reservation_revision BEFORE INSERT OR UPDATE ON "Reservations"
                    FOR EACH ROW EXECUTE FUNCTION rotate_reservation_revision();

                CREATE FUNCTION rotate_reservation_tables_revision() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        UPDATE "Reservations" SET revision = gen_random_uuid() WHERE id = OLD.reservation_id;
                    ELSIF TG_OP = 'INSERT' THEN
                        UPDATE "Reservations" SET revision = gen_random_uuid() WHERE id = NEW.reservation_id;
                    ELSE
                        UPDATE "Reservations" SET revision = gen_random_uuid()
                            WHERE id IN (OLD.reservation_id, NEW.reservation_id);
                    END IF;
                    RETURN NULL;
                END $$;
                CREATE TRIGGER reservation_tables_revision AFTER INSERT OR UPDATE OR DELETE ON "ReservationTables"
                    FOR EACH ROW EXECUTE FUNCTION rotate_reservation_tables_revision();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Never wait while holding one side of the booking/receipt lock order.
                LOCK TABLE "Reservations", "ReservationTables", customer_reservation_operations
                    IN ACCESS EXCLUSIVE MODE NOWAIT;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM customer_reservation_operations) THEN
                        RAISE EXCEPTION 'Reservation revision protects permanent operation history' USING ERRCODE = '23514';
                    END IF;
                END $$;
                DROP TRIGGER reservation_tables_revision ON "ReservationTables";
                DROP TRIGGER reservation_revision ON "Reservations";
                DROP FUNCTION rotate_reservation_tables_revision();
                DROP FUNCTION rotate_reservation_revision();
                """);
            migrationBuilder.DropColumn(
                name: "revision",
                table: "Reservations");
        }
    }
}
