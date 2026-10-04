using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261004105734_AddCashHistoryCapacityRefusal")] // pragma: allowlist secret
    public partial class AddCashHistoryCapacityRefusal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_amendment_resolution_refusal_code",
                table: "order_amendment_resolution_refusals");

            migrationBuilder.AddCheckConstraint(
                name: "ck_amendment_resolution_refusal_code",
                table: "order_amendment_resolution_refusals",
                sql: "failure_code IN ('quoteExpired','sourceVersionConflict','accountRevisionConflict','quoteChanged','cashHistoryCapacityExceeded')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE order_amendment_resolution_refusals IN ACCESS EXCLUSIVE MODE;
                DO $guard$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM order_amendment_resolution_refusals
                        WHERE failure_code = 'cashHistoryCapacityExceeded'
                    ) THEN
                        RAISE EXCEPTION 'Cash history capacity refusal history must be retained'
                            USING ERRCODE = '23514';
                    END IF;
                END;
                $guard$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_amendment_resolution_refusal_code",
                table: "order_amendment_resolution_refusals");

            migrationBuilder.AddCheckConstraint(
                name: "ck_amendment_resolution_refusal_code",
                table: "order_amendment_resolution_refusals",
                sql: "failure_code IN ('quoteExpired','sourceVersionConflict','accountRevisionConflict','quoteChanged')");
        }
    }
}
