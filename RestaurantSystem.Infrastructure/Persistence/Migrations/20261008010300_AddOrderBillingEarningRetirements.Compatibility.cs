using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddOrderBillingEarningRetirements
{
    private static void ProtectOrderBillingEarningRetirements(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE FUNCTION validate_order_billing_earning_retirement() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE
              source orders%ROWTYPE;
              snapshot order_billing_snapshots%ROWTYPE;
              amendment order_amendments%ROWTYPE;
              expected_units bigint;
              stored_units bigint;
            BEGIN
              IF TG_OP <> 'INSERT' THEN
                RAISE EXCEPTION 'Order billing earning retirements are immutable' USING ERRCODE = '23514';
              END IF;

              SELECT * INTO source FROM orders WHERE id = NEW.order_id FOR KEY SHARE;
              IF NOT FOUND OR source.is_deleted OR source.fidelity_points_earned <> 0 THEN
                RAISE EXCEPTION 'An earning retirement requires its live zero-award source order' USING ERRCODE = '23514';
              END IF;

              SELECT * INTO snapshot FROM order_billing_snapshots WHERE order_id = NEW.order_id FOR KEY SHARE;
              IF NOT FOUND OR snapshot.id IS DISTINCT FROM NEW.snapshot_id
                 OR snapshot.earned_points_candidate IS NOT NULL
                 OR snapshot.earning_disposition IS NOT NULL AND snapshot.earning_disposition <> 'Unevaluated' THEN
                RAISE EXCEPTION 'An earning retirement requires the exact unevaluated accepted snapshot' USING ERRCODE = '23514';
              END IF;

              IF EXISTS (SELECT 1 FROM order_billing_award_witnesses WHERE order_id = NEW.order_id)
                 OR EXISTS (SELECT 1 FROM order_billing_award_unit_coverages WHERE order_id = NEW.order_id)
                 OR EXISTS (SELECT 1 FROM order_billing_unit_award_suppressions WHERE order_id = NEW.order_id)
                 OR EXISTS (SELECT 1 FROM fidelity_points_transactions
                            WHERE order_id = NEW.order_id AND transaction_type = 'Earned')
                 OR EXISTS (SELECT 1 FROM order_amendment_loyalty_compensations
                            WHERE source_order_id = NEW.order_id)
                 OR EXISTS (SELECT 1 FROM order_amendment_resolution_operations
                            WHERE source_order_id = NEW.order_id) THEN
                RAISE EXCEPTION 'Award or amendment settlement evidence already exists for this source order' USING ERRCODE = '23514';
              END IF;

              SELECT * INTO amendment FROM order_amendments
              WHERE id = NEW.amendment_id AND source_order_id = NEW.order_id FOR KEY SHARE;
              IF NOT FOUND OR amendment.state <> 'Committed'
                 OR amendment.service_session_id IS DISTINCT FROM source.service_session_id
                 OR amendment.supplement_order_id IS NOT NULL OR amendment.committed_at IS NULL
                 OR jsonb_typeof(amendment.changes_json) IS DISTINCT FROM 'array'
                 OR jsonb_array_length(amendment.changes_json) = 0
                 OR EXISTS (SELECT 1 FROM "ExternalOrderReferences" WHERE order_id = NEW.order_id) THEN
                RAISE EXCEPTION 'An earning retirement requires one committed native full-source amendment' USING ERRCODE = '23514';
              END IF;

              IF EXISTS (
                  SELECT 1 FROM jsonb_array_elements(amendment.changes_json) AS change(value)
                  LEFT JOIN "OrderItems" item
                    ON item.order_id = NEW.order_id
                   AND item.id::text = change.value ->> 'orderItemId'
                   AND item.parent_order_item_id IS NULL
                  WHERE jsonb_typeof(change.value) IS DISTINCT FROM 'object'
                     OR change.value ->> 'kind' IS DISTINCT FROM 'Void'
                     OR COALESCE(change.value -> 'current', 'null'::jsonb) <> 'null'::jsonb
                     OR change.value ->> 'replacementDispatchedOrderId' IS NOT NULL
                     OR change.value ->> 'replacementDispatchedOrderNumber' IS NOT NULL
                     OR item.id IS NULL
                     OR change.value -> 'previous' ->> 'id' IS DISTINCT FROM item.id::text
                     OR change.value -> 'previous' ->> 'quantity' IS DISTINCT FROM item.quantity::text
                     OR COALESCE(change.value ->> 'startOrdinal', '') !~ '^[0-9]{1,10}$'
                     OR COALESCE(change.value ->> 'quantity', '') !~ '^[0-9]{1,10}$'
                     OR (change.value ->> 'startOrdinal')::bigint < 1
                     OR (change.value ->> 'quantity')::bigint < 1
                     OR (change.value ->> 'startOrdinal')::bigint
                          + (change.value ->> 'quantity')::bigint - 1 > item.quantity
              ) THEN
                RAISE EXCEPTION 'An earning retirement amendment contains an invalid or non-void source range' USING ERRCODE = '23514';
              END IF;

              IF EXISTS (
                  SELECT 1 FROM order_amendments prior
                  WHERE prior.source_order_id = NEW.order_id AND prior.state = 'Committed'
                    AND prior.id <> NEW.amendment_id
                    AND (jsonb_typeof(prior.changes_json) IS DISTINCT FROM 'array'
                      OR EXISTS (SELECT 1 FROM jsonb_array_elements(
                          CASE WHEN jsonb_typeof(prior.changes_json) = 'array'
                               THEN prior.changes_json ELSE '[]'::jsonb END) AS change(value)
                          WHERE change.value ->> 'kind' IN ('Void', 'Replace')))
              ) THEN
                RAISE EXCEPTION 'An earning retirement cannot follow another source-unit removal' USING ERRCODE = '23514';
              END IF;

              SELECT COALESCE(SUM(item.quantity), 0) INTO expected_units
              FROM "OrderItems" item
              WHERE item.order_id = NEW.order_id AND item.parent_order_item_id IS NULL;
              SELECT COUNT(*) INTO stored_units FROM order_billing_snapshot_units
              WHERE order_id = NEW.order_id;
              IF expected_units <= 0 OR expected_units <> stored_units
                 OR expected_units <> NEW.retired_unit_count
                 OR EXISTS (
                    SELECT 1 FROM order_billing_snapshot_units unit
                    WHERE unit.order_id = NEW.order_id
                      AND (SELECT COUNT(*) FROM jsonb_array_elements(amendment.changes_json) AS change(value)
                           WHERE order_billing_amendment_change_removes_unit(
                               change.value, unit.order_item_id, unit.unit_ordinal)) <> 1)
                 OR EXISTS (
                    SELECT 1 FROM "OrderItems" item
                    WHERE item.order_id = NEW.order_id AND item.parent_order_item_id IS NULL
                      AND NOT EXISTS (SELECT 1 FROM order_billing_snapshot_units unit
                                      WHERE unit.order_id = NEW.order_id
                                        AND unit.order_item_id = item.id)) THEN
                RAISE EXCEPTION 'An earning retirement must cover every accepted source unit exactly once' USING ERRCODE = '23514';
              END IF;

              IF NEW.id = '00000000-0000-0000-0000-000000000000'::uuid
                 OR NEW.retired_unit_count <= 0
                 OR NEW.created_by IS DISTINCT FROM 'OrderBillingEarningRetirementService'
                 OR NEW.updated_at IS NOT NULL OR NEW.updated_by IS NOT NULL THEN
                RAISE EXCEPTION 'An earning retirement has invalid immutable journal metadata' USING ERRCODE = '23514';
              END IF;
              RETURN NEW;
            END $$;
            CREATE TRIGGER validate_order_billing_earning_retirement
              BEFORE INSERT OR UPDATE OR DELETE ON order_billing_earning_retirements
              FOR EACH ROW EXECUTE FUNCTION validate_order_billing_earning_retirement();
            """);

    private static void RemoveOrderBillingEarningRetirements(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TRIGGER validate_order_billing_earning_retirement ON order_billing_earning_retirements;
            DROP FUNCTION validate_order_billing_earning_retirement();
            """);
}
