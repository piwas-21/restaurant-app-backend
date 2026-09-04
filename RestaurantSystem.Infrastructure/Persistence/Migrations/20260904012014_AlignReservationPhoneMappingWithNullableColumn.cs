using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Backend #420. A NO-OP against every deployed database, and that is the point: the column has
    /// been nullable since <c>20251102031347_MakeCustomerPhoneOptional</c> (verified on production
    /// 2026-09-04: <c>is_nullable=YES</c>, 2 of 19 rows already NULL). What was out of step was the
    /// MODEL — <c>ReservationConfiguration</c> still mapped the property <c>IsRequired()</c>, so EF
    /// materialised a nullable column with a non-null read and one NULL row threw, hiding every
    /// reservation from the dashboard.
    /// <para>
    /// This migration exists only so the model snapshot records what the schema already is.
    /// Applying it changes no data and no schema; not having it makes EF refuse to start with
    /// <c>PendingModelChangesWarning</c>.
    /// </para>
    /// </summary>
    public partial class AlignReservationPhoneMappingWithNullableColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "customer_phone",
                table: "Reservations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }

        /// <summary>
        /// EMPTY, deliberately. A migration whose <c>Up()</c> only synchronises the snapshot has
        /// nothing to reverse — the column was already nullable before it and is still nullable
        /// after it.
        /// <para>
        /// The scaffolded body was actively dangerous and did not fail, which is worse. Npgsql emits
        /// the backfill itself, so it would have run
        /// <c>UPDATE "Reservations" SET customer_phone = '' WHERE customer_phone IS NULL</c> and then
        /// <c>SET NOT NULL</c> — irreversibly rewriting production's phoneless bookings ("no phone"
        /// and "the empty string" are different facts) and leaving the column MORE constrained than
        /// it has been since 20251102031347, which re-creates the defect #420 exists to remove. It
        /// also left a <c>DEFAULT ''</c> behind that <c>Up()</c> never drops.
        /// </para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
