using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerTablePaymentOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_table_payment_operations",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_stopped = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_table_payment_operations", x => x.operation_id);
                    table.CheckConstraint("ck_customer_table_payment_operations_outcome", "(is_stopped AND attempt_id IS NULL) OR (NOT is_stopped AND attempt_id IS NOT NULL AND attempt_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_table_payment_operations_attempt_id",
                table: "customer_table_payment_operations",
                column: "attempt_id",
                unique: true,
                filter: "attempt_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE customer_table_payment_operations IN ACCESS EXCLUSIVE MODE NOWAIT;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM customer_table_payment_operations) THEN
                        RAISE EXCEPTION 'Native contribution decisions must be retained' USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "customer_table_payment_operations");
        }
    }
}
