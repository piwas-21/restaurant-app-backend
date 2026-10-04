using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddAccountCashRefundEvidence
{
    private static void ProtectAccountCashRefundHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION prevent_account_cash_refund_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Account cash refund history is immutable' USING ERRCODE = '23514'; END $$;
        CREATE TRIGGER immutable_account_cash_refund_intents
          BEFORE UPDATE OR DELETE ON account_cash_refund_intents
          FOR EACH ROW EXECUTE FUNCTION prevent_account_cash_refund_history_mutation();
        CREATE TRIGGER immutable_account_cash_refund_evidence
          BEFORE UPDATE OR DELETE ON account_cash_refund_evidence
          FOR EACH ROW EXECUTE FUNCTION prevent_account_cash_refund_history_mutation();
        """);

    private static void RequireNoAccountCashRefundHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE account_cash_refund_evidence, account_cash_refund_intents IN ACCESS EXCLUSIVE MODE;
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM account_cash_refund_evidence)
            OR EXISTS (SELECT 1 FROM account_cash_refund_intents) THEN
            RAISE EXCEPTION 'Account cash refund history must be retained' USING ERRCODE = '23514';
          END IF;
        END $$;
        DROP TRIGGER immutable_account_cash_refund_evidence ON account_cash_refund_evidence;
        DROP TRIGGER immutable_account_cash_refund_intents ON account_cash_refund_intents;
        DROP FUNCTION prevent_account_cash_refund_history_mutation();
        """);
}
