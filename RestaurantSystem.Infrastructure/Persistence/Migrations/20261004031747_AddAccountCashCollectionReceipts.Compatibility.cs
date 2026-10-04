using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddAccountCashCollectionReceipts
{
    private static void ProtectAccountCashCollectionReceiptHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION prevent_account_cash_collection_receipt_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Account cash collection receipt history is immutable' USING ERRCODE = '23514'; END $$;
        CREATE TRIGGER immutable_account_cash_collection_receipts
          BEFORE UPDATE OR DELETE ON account_cash_collection_receipts
          FOR EACH ROW EXECUTE FUNCTION prevent_account_cash_collection_receipt_mutation();
        """);

    private static void RequireNoAccountCashCollectionReceiptHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE account_cash_collection_receipts IN ACCESS EXCLUSIVE MODE;
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM account_cash_collection_receipts) THEN
            RAISE EXCEPTION 'Account cash collection receipt history must be retained' USING ERRCODE = '23514';
          END IF;
        END $$;
        DROP TRIGGER immutable_account_cash_collection_receipts ON account_cash_collection_receipts;
        DROP FUNCTION prevent_account_cash_collection_receipt_mutation();
        """);
}
