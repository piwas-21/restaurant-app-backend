using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBasketCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_cancelled",
                table: "customer_basket_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Removing cancellation decisions would let a late original request execute again.
            // Use the same guard for every receipt so multi-step historical downgrades remain atomic.
            migrationBuilder.Sql("""
                LOCK TABLE customer_basket_operations IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM customer_basket_operations)
                    THEN
                        RAISE EXCEPTION 'Customer basket operation history must be retained' USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "is_cancelled",
                table: "customer_basket_operations");
        }
    }
}
