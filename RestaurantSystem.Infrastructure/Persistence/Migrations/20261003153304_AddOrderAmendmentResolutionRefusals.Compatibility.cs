using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddOrderAmendmentResolutionRefusals
{
    private static void ProtectRefusalHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION prevent_order_amendment_resolution_refusal_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Amendment resolution refusal history is immutable' USING ERRCODE = '23514'; END $$;
        CREATE TRIGGER immutable_order_amendment_resolution_refusals
          BEFORE UPDATE OR DELETE ON order_amendment_resolution_refusals
          FOR EACH ROW EXECUTE FUNCTION prevent_order_amendment_resolution_refusal_mutation();
        """);

    private static void RequireNoRefusalHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE order_amendment_resolution_operations,
          order_amendment_refund_legs,
          order_amendment_refund_attempts,
          order_amendment_refund_evidence,
          account_payment_allocation_reversals,
          order_amendment_resolution_refusals
          IN ACCESS EXCLUSIVE MODE;
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM order_amendment_resolution_refusals) THEN
            RAISE EXCEPTION 'Amendment resolution refusal history must be retained' USING ERRCODE = '23514';
          END IF;
          IF EXISTS (SELECT 1 FROM order_amendment_resolution_operations)
             OR EXISTS (SELECT 1 FROM order_amendment_refund_legs)
             OR EXISTS (SELECT 1 FROM order_amendment_refund_attempts)
             OR EXISTS (SELECT 1 FROM order_amendment_refund_evidence)
             OR EXISTS (SELECT 1 FROM account_payment_allocation_reversals) THEN
            RAISE EXCEPTION 'Paid amendment financial history must be retained' USING ERRCODE = '23514';
          END IF;
        END $$;
        DROP TRIGGER immutable_order_amendment_resolution_refusals ON order_amendment_resolution_refusals;
        DROP FUNCTION prevent_order_amendment_resolution_refusal_mutation();
        """);
}
