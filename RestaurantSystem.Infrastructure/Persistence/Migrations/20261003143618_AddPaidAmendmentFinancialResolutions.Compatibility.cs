using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddPaidAmendmentFinancialResolutions
{
    private static void ProtectPaidFinancialHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION prevent_paid_amendment_financial_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Paid amendment financial history is immutable' USING ERRCODE = '23514'; END $$;
        CREATE TRIGGER immutable_order_amendment_refund_evidence
          BEFORE UPDATE OR DELETE ON order_amendment_refund_evidence
          FOR EACH ROW EXECUTE FUNCTION prevent_paid_amendment_financial_history_mutation();
        CREATE TRIGGER immutable_account_payment_allocation_reversals
          BEFORE UPDATE OR DELETE ON account_payment_allocation_reversals
          FOR EACH ROW EXECUTE FUNCTION prevent_paid_amendment_financial_history_mutation();
        CREATE TRIGGER immutable_order_amendment_refund_attempts
          BEFORE UPDATE OR DELETE ON order_amendment_refund_attempts
          FOR EACH ROW EXECUTE FUNCTION prevent_paid_amendment_financial_history_mutation();
        """);

    private static void RequireNoPaidFinancialHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE order_amendment_resolution_operations,
          order_amendment_refund_legs,
          order_amendment_refund_attempts,
          order_amendment_refund_evidence,
          account_payment_allocation_reversals
          IN ACCESS EXCLUSIVE MODE;
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM order_amendment_resolution_operations)
             OR EXISTS (SELECT 1 FROM order_amendment_refund_legs)
             OR EXISTS (SELECT 1 FROM order_amendment_refund_attempts)
             OR EXISTS (SELECT 1 FROM order_amendment_refund_evidence)
             OR EXISTS (SELECT 1 FROM account_payment_allocation_reversals) THEN
            RAISE EXCEPTION 'Paid amendment financial history must be retained' USING ERRCODE = '23514';
          END IF;
        END $$;
        DROP TRIGGER immutable_order_amendment_refund_evidence ON order_amendment_refund_evidence;
        DROP TRIGGER immutable_account_payment_allocation_reversals ON account_payment_allocation_reversals;
        DROP TRIGGER immutable_order_amendment_refund_attempts ON order_amendment_refund_attempts;
        DROP FUNCTION prevent_paid_amendment_financial_history_mutation();
        """);
}
