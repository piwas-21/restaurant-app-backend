using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddOrderBillingCredits
{
    private static void ApplyCompatibilityBackfill(MigrationBuilder migrationBuilder)
    {
        PrepareProvenLegacyCreditScopes(migrationBuilder);
        // Only an unused visit can adopt food/tip/fee allocation. Every durable scope stays model 0.
        migrationBuilder.Sql("""
            UPDATE table_service_sessions s SET billing_allocation_version = 1
            WHERE NOT EXISTS (SELECT 1 FROM account_payment_attempts a WHERE a.service_session_id = s.id)
              AND NOT EXISTS (SELECT 1 FROM account_equal_share_plans p WHERE p.service_session_id = s.id)
              AND NOT EXISTS (SELECT 1 FROM orders o WHERE o.service_session_id = s.id)
              AND NOT EXISTS (SELECT 1 FROM order_amendments a WHERE a.service_session_id = s.id AND a.state = 'Committed')
              AND NOT EXISTS (
                SELECT 1 FROM orders o WHERE o.service_session_id = s.id
                AND (o.total_paid <> 0
                  OR EXISTS (SELECT 1 FROM order_payments p WHERE p.order_id = o.id)
                  OR EXISTS (SELECT 1 FROM order_checkout_sessions c WHERE c.order_id = o.id)));

            -- A previous unpaid resolved credit was only a JSON outcome. Materialize only sources
            -- with no monetary history and no tax/tip/fee/loyalty ambiguity; everything else stays
            -- unmaterialized so the new consistency guard requires explicit reconciliation.
            CREATE TEMP TABLE proven_order_credit_backfill ON COMMIT DROP AS
            WITH candidates AS (
              SELECT a.id, a.source_order_id, a.actor_user_id, a.actor_role, a.created_at, a.created_by,
                a.financial_resolution_json ->> 'currency' AS currency,
                CASE WHEN jsonb_typeof(a.financial_resolution_json -> 'potentialCreditMinor') = 'number'
                  AND (a.financial_resolution_json ->> 'potentialCreditMinor') ~ '^[0-9]+$'
                  THEN (a.financial_resolution_json ->> 'potentialCreditMinor')::numeric END AS amount_minor
              FROM order_amendments a JOIN orders o ON o.id = a.source_order_id
                JOIN table_service_sessions s ON s.id = o.service_session_id
              WHERE a.state = 'Committed' AND NOT o.is_deleted AND o.type = 'DineIn'
                AND a.id IN (SELECT amendment_id FROM proven_legacy_credit_scopes)
                AND a.service_session_id = s.id
                AND s.currency = a.financial_resolution_json ->> 'currency'
                AND a.financial_resolution_json ->> 'resolutionStatus' = 'Resolved'
                AND a.financial_resolution_json ->> 'creditState' = 'BalanceReduction'
                AND a.financial_resolution_json ->> 'loyaltyState' = 'None'
                AND a.financial_resolution_json ->> 'refundState' = 'None'
                AND a.actor_user_id <> '00000000-0000-0000-0000-000000000000'::uuid
                AND a.actor_role IN ('Admin', 'Cashier', 'Server')
                AND o.total_paid = 0 AND o.tip = 0 AND o.delivery_fee = 0 AND o.tax = 0
                AND o.fidelity_points_earned = 0 AND o.fidelity_points_redeemed = 0 AND o.fidelity_points_discount = 0
                AND NOT EXISTS (SELECT 1 FROM "ExternalOrderReferences" e WHERE e.order_id = o.id)
                AND NOT EXISTS (SELECT 1 FROM order_payments p WHERE p.order_id = o.id)
                AND NOT EXISTS (SELECT 1 FROM order_checkout_sessions c WHERE c.order_id = o.id)
                AND NOT EXISTS (SELECT 1 FROM account_payment_attempts p WHERE p.service_session_id = o.service_session_id)
                AND NOT EXISTS (SELECT 1 FROM account_equal_share_plans p WHERE p.service_session_id = o.service_session_id)
            ), bounded AS (
              SELECT c.*, sum(amount_minor) OVER (PARTITION BY source_order_id) AS source_credit_minor
              FROM candidates c WHERE amount_minor > 0 AND amount_minor <= 9999999999
                AND currency IN ('CHF', 'EUR', 'GBP', 'USD', 'AED')
            )
            SELECT b.* FROM bounded b JOIN orders o ON o.id = b.source_order_id
              JOIN order_amendments a ON a.id = b.id
            WHERE b.source_credit_minor <= o.total * 100
              AND jsonb_typeof(a.financial_resolution_json -> 'removedUnitValueMinor') = 'number'
              AND a.financial_resolution_json -> 'removedUnitValueMinor' = a.financial_resolution_json -> 'potentialCreditMinor'
              AND jsonb_typeof(a.financial_resolution_json -> 'addedAmountMinor') = 'number'
              AND (a.financial_resolution_json ->> 'addedAmountMinor') ~ '^[0-9]+$'
              AND jsonb_typeof(a.financial_resolution_json -> 'netAccountDeltaMinor') = 'number'
              AND (a.financial_resolution_json ->> 'netAccountDeltaMinor') ~ '^-?[0-9]+$'
              AND CASE WHEN (a.financial_resolution_json ->> 'addedAmountMinor') ~ '^[0-9]+$'
                    AND (a.financial_resolution_json ->> 'netAccountDeltaMinor') ~ '^-?[0-9]+$'
                  THEN (a.financial_resolution_json ->> 'netAccountDeltaMinor')::numeric
                    = (a.financial_resolution_json ->> 'addedAmountMinor')::numeric - b.amount_minor
                  ELSE false END;

            INSERT INTO order_billing_credits
              (id, source_order_id, amendment_id, amount_minor, currency, actor_user_id, actor_role, created_at, created_by)
            SELECT gen_random_uuid(), source_order_id, id, amount_minor::bigint, currency,
              actor_user_id, actor_role, created_at, created_by FROM proven_order_credit_backfill;

            UPDATE orders o SET billing_credit_amount = c.amount / 100,
              remaining_amount = o.total - c.amount / 100,
              payment_status = CASE WHEN o.total = c.amount / 100 THEN 'Completed' ELSE 'Pending' END
            FROM (SELECT source_order_id, sum(amount_minor) AS amount
              FROM proven_order_credit_backfill GROUP BY source_order_id) c
            WHERE o.id = c.source_order_id;
            """);
    }

    private static void ProtectBillingHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION reject_order_billing_credit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Billing credit history is immutable' USING ERRCODE = '23514'; END $$;
        CREATE TRIGGER immutable_order_billing_credits BEFORE UPDATE OR DELETE ON order_billing_credits
          FOR EACH ROW EXECUTE FUNCTION reject_order_billing_credit_mutation();
        CREATE FUNCTION reject_billing_allocation_version_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW.billing_allocation_version IS DISTINCT FROM OLD.billing_allocation_version THEN
            RAISE EXCEPTION 'The visit allocation version is immutable' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER immutable_billing_allocation_version BEFORE UPDATE ON table_service_sessions
          FOR EACH ROW EXECUTE FUNCTION reject_billing_allocation_version_mutation();
        """);

    private static void RequireEmptyBillingHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM order_billing_credits)
            OR EXISTS (SELECT 1 FROM orders WHERE billing_credit_amount <> 0) THEN
            RAISE EXCEPTION 'Billing history exists; disable the feature instead of removing money records';
          END IF;
          IF EXISTS (
            SELECT 1 FROM table_service_sessions s WHERE s.billing_allocation_version = 1
              AND (EXISTS (SELECT 1 FROM account_payment_attempts a WHERE a.service_session_id = s.id)
                OR EXISTS (SELECT 1 FROM account_equal_share_plans p WHERE p.service_session_id = s.id)
                OR EXISTS (SELECT 1 FROM order_amendments a WHERE a.service_session_id = s.id AND a.state = 'Committed')
                OR EXISTS (SELECT 1 FROM orders o WHERE o.service_session_id = s.id))) THEN
            RAISE EXCEPTION 'Current visit allocation history exists; retain its version and disable the feature';
          END IF;
        END $$;
        DROP TRIGGER immutable_order_billing_credits ON order_billing_credits;
        DROP FUNCTION reject_order_billing_credit_mutation();
        DROP TRIGGER immutable_billing_allocation_version ON table_service_sessions;
        DROP FUNCTION reject_billing_allocation_version_mutation();
        """);
}
