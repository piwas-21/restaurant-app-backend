using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyOrderOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "business_date",
                table: "orders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "daily_order_number",
                table: "orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "effective_preparation_minutes",
                table: "orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "expected_ready_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "prior_day_review_dismissed_at",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prior_day_review_dismissed_by",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "preparation_time_minutes_snapshot",
                table: "OrderItems",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE orders
                SET business_date = try_order_number_day(order_number)
                WHERE try_order_number_day(order_number) IS NOT NULL
                  AND try_order_number_sequence(order_number) IS NOT NULL;

                -- A previous delivery estimate is a ready-time promise for confirmed and
                -- preparing takeaway/dine-in orders. Delivery ETAs belong to the courier and
                -- pending orders have not started their preparation countdown.
                UPDATE orders
                SET expected_ready_at = estimated_delivery_time
                WHERE estimated_delivery_time IS NOT NULL
                  AND status IN ('Confirmed', 'Preparing')
                  AND type <> 'Delivery';

                CREATE FUNCTION set_order_daily_identity()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    parsed_day date;
                    parsed_sequence bigint;
                BEGIN
                    IF NEW.business_date IS NULL OR NEW.business_date = DATE '0001-01-01' THEN
                        parsed_day := try_order_number_day(NEW.order_number);
                        NEW.business_date := COALESCE(
                            parsed_day,
                            (NEW.order_date AT TIME ZONE current_setting('TimeZone'))::date);
                    END IF;
                    IF NEW.daily_order_number IS NULL OR NEW.daily_order_number = 0 THEN
                        parsed_day := try_order_number_day(NEW.order_number);
                        parsed_sequence := try_order_number_sequence(NEW.order_number);
                        IF parsed_sequence IS NOT NULL AND parsed_day = NEW.business_date THEN
                            NEW.daily_order_number := parsed_sequence;
                        ELSE
                            INSERT INTO order_number_sequences (day, last_sequence)
                            VALUES (NEW.business_date, 1)
                            ON CONFLICT (day) DO UPDATE
                            SET last_sequence = order_number_sequences.last_sequence + 1
                            RETURNING last_sequence INTO NEW.daily_order_number;
                        END IF;
                    END IF;

                    -- Older application instances already advanced this table for generated
                    -- order numbers, but direct/legacy inserts can supply the suffix without
                    -- touching the watermark. Keep the durable sequence at least as high as every
                    -- inserted identity so a later writer cannot reuse an existing number.
                    INSERT INTO order_number_sequences (day, last_sequence)
                    VALUES (NEW.business_date, NEW.daily_order_number)
                    ON CONFLICT (day) DO UPDATE
                    SET last_sequence = GREATEST(
                        order_number_sequences.last_sequence, EXCLUDED.last_sequence);

                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER orders_set_daily_identity
                BEFORE INSERT ON orders
                FOR EACH ROW
                EXECUTE FUNCTION set_order_daily_identity();
                """);

            migrationBuilder.CreateIndex(
                name: "ix_orders_business_date",
                table: "orders",
                column: "business_date");

            migrationBuilder.CreateIndex(
                name: "ix_orders_business_date_daily_order_number",
                table: "orders",
                columns: new[] { "business_date", "daily_order_number" },
                unique: true,
                filter: "\"business_date\" IS NOT NULL AND \"daily_order_number\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_items_preparation_snapshot_nonnegative",
                table: "OrderItems",
                sql: "preparation_time_minutes_snapshot IS NULL OR preparation_time_minutes_snapshot >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS orders_set_daily_identity ON orders;
                DROP FUNCTION IF EXISTS set_order_daily_identity();
                """);

            migrationBuilder.DropIndex(
                name: "ix_orders_business_date",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_business_date_daily_order_number",
                table: "orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_items_preparation_snapshot_nonnegative",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "business_date",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "daily_order_number",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "effective_preparation_minutes",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "expected_ready_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "prior_day_review_dismissed_at",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "prior_day_review_dismissed_by",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "preparation_time_minutes_snapshot",
                table: "OrderItems");
        }
    }
}
