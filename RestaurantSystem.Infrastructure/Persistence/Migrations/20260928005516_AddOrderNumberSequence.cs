using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_number_sequences",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_number_sequences", x => x.day);
                    table.CheckConstraint("ck_order_number_sequences_last_sequence", "last_sequence >= 0");
                });

            migrationBuilder.Sql("""
                CREATE FUNCTION try_order_number_day(order_number_value text)
                RETURNS date
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    day_prefix text;
                    parsed_day date;
                BEGIN
                    IF order_number_value !~ '^[0-9]{8}[0-9]{4,}$' THEN
                        RETURN NULL;
                    END IF;

                    day_prefix := substring(order_number_value FROM 1 FOR 8);
                    BEGIN
                        parsed_day := to_date(day_prefix, 'YYYYMMDD');
                    EXCEPTION
                        WHEN datetime_field_overflow OR invalid_datetime_format THEN
                            RETURN NULL;
                    END;

                    IF to_char(parsed_day, 'YYYYMMDD') <> day_prefix THEN
                        RETURN NULL;
                    END IF;

                    RETURN parsed_day;
                END;
                $function$;

                CREATE FUNCTION try_order_number_sequence(order_number_value text)
                RETURNS bigint
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    sequence_suffix text;
                BEGIN
                    IF order_number_value !~ '^[0-9]{8}[0-9]{4,}$' THEN
                        RETURN NULL;
                    END IF;

                    sequence_suffix := substring(order_number_value FROM 9);
                    IF length(sequence_suffix) > 19 THEN
                        RETURN NULL;
                    END IF;

                    BEGIN
                        RETURN sequence_suffix::bigint;
                    EXCEPTION
                        WHEN numeric_value_out_of_range THEN
                            RETURN NULL;
                    END;
                END;
                $function$;

                CREATE FUNCTION record_order_number_sequence()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    order_day date;
                    order_sequence bigint;
                BEGIN
                    order_day := try_order_number_day(NEW.order_number);
                    order_sequence := try_order_number_sequence(NEW.order_number);
                    IF order_day IS NOT NULL AND order_sequence IS NOT NULL THEN
                        INSERT INTO order_number_sequences (day, last_sequence)
                        VALUES (order_day, order_sequence)
                        ON CONFLICT (day) DO UPDATE
                        SET last_sequence = GREATEST(
                            order_number_sequences.last_sequence,
                            EXCLUDED.last_sequence);
                    END IF;

                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER orders_record_number_sequence
                AFTER INSERT ON orders
                FOR EACH ROW
                EXECUTE FUNCTION record_order_number_sequence();

                INSERT INTO order_number_sequences (day, last_sequence)
                SELECT parsed.order_day, MAX(parsed.order_sequence)
                FROM (
                    SELECT
                        try_order_number_day(order_number) AS order_day,
                        try_order_number_sequence(order_number) AS order_sequence
                    FROM orders
                ) AS parsed
                WHERE parsed.order_day IS NOT NULL
                  AND parsed.order_sequence IS NOT NULL
                GROUP BY parsed.order_day
                ON CONFLICT (day) DO UPDATE
                SET last_sequence = GREATEST(
                    order_number_sequences.last_sequence,
                    EXCLUDED.last_sequence);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS orders_record_number_sequence ON orders;
                DROP FUNCTION IF EXISTS record_order_number_sequence();
                DROP FUNCTION IF EXISTS try_order_number_sequence(text);
                DROP FUNCTION IF EXISTS try_order_number_day(text);
                """);

            migrationBuilder.DropTable(
                name: "order_number_sequences");
        }
    }
}
