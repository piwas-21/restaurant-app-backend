using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerOrderCancellationOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_order_cancellation_operations",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expected_version = table.Column<int>(type: "integer", nullable: false),
                    observed_version = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_order_cancellation_operations", x => x.operation_id);
                    table.CheckConstraint("ck_customer_order_cancellation_operation_shape", "operation_id <> '00000000-0000-0000-0000-000000000000'::uuid AND order_id <> '00000000-0000-0000-0000-000000000000'::uuid AND owner_hash ~ '^[0-9a-f]{64}$' AND payload_hash ~ '^[0-9a-f]{64}$' AND expected_version > 0 AND observed_version > 0 AND ((outcome = 'Cancelled' AND reason IS NULL AND observed_version > expected_version) OR (outcome = 'Refused' AND reason IS NOT NULL AND reason IN ('OrderChanged', 'NotAwaitingCustomerApproval', 'RestaurantManaged', 'FinancialReview', 'OrderingUnavailable')))");
                });

            migrationBuilder.Sql("""
                CREATE FUNCTION reject_customer_order_cancellation_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Customer order cancellation decisions are immutable' USING ERRCODE = '23514';
                END $$;
                CREATE TRIGGER immutable_customer_order_cancellation_operations
                  BEFORE UPDATE OR DELETE ON customer_order_cancellation_operations
                  FOR EACH ROW EXECUTE FUNCTION reject_customer_order_cancellation_mutation();
                """);

            migrationBuilder.CreateIndex(
                name: "ix_customer_order_cancellation_operations_order_id",
                table: "customer_order_cancellation_operations",
                column: "order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                LOCK TABLE customer_order_cancellation_operations IN ACCESS EXCLUSIVE MODE;
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM customer_order_cancellation_operations)
                    THEN
                        RAISE EXCEPTION 'Customer order cancellation decisions must be retained' USING ERRCODE = '23514';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "customer_order_cancellation_operations");
            migrationBuilder.Sql("DROP FUNCTION reject_customer_order_cancellation_mutation();");
        }
    }
}
