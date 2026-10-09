using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPaymentTips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "refunded_tip_minor",
                table: "order_payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "tip_minor",
                table: "order_payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE order_payments IN ACCESS EXCLUSIVE MODE;
                DO $$ BEGIN
                  IF EXISTS (
                    SELECT 1 FROM order_payments
                    WHERE tip_minor <> 0 OR refunded_tip_minor <> 0
                  ) THEN
                    RAISE EXCEPTION 'Order payment gratuity history must be retained; use a forward migration'
                      USING ERRCODE = '23514';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "refunded_tip_minor",
                table: "order_payments");

            migrationBuilder.DropColumn(
                name: "tip_minor",
                table: "order_payments");
        }
    }
}
